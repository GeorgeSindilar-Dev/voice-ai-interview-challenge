using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VoiceReset.Storage;

namespace VoiceReset.Mock;

public sealed partial class MockIssuer(IJsonStore store, IOptions<MockOptions> options, TimeProvider time) : IDisposable
{
    public const string StateKey = "mock/state.json";
    private static readonly TimeSpan s_codeLifetime = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan s_linkLifetime = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private MockState _state = new();

    public string ServiceCredential => options.Value.ServiceCredential;

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // Constant time; hashing first makes the lengths equal.
    public static bool SecretEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    public MockUser? FindUser(string username) =>
        options.Value.Users.FirstOrDefault(u => Normalize(u.Username) == Normalize(username));

    public async Task LoadAsync(CancellationToken ct) =>
        _state = await store.ReadAsync<MockState>(StateKey, ct) ?? new();

    public void Dispose() => _lock.Dispose();

    // Messages are appended in order, so reversing gives newest first (even when they share a timestamp).
    public Task<IReadOnlyList<InboxMessage>> GetInboxAsync(string username, CancellationToken ct) =>
        RunAsync<IReadOnlyList<InboxMessage>>(
            _ => [.. _state.Inbox.Where(m => m.Username == Normalize(username)).Reverse()], save: false, ct);

    public Task<MockResult> StartRecoveryAsync(StartRecoveryRequest request, CancellationToken ct) =>
        RunAsync(now => StartRecovery(request, now), save: true, ct);

    public Task<MockResult> VerifyAsync(string recoveryId, string idempotencyKey, VerifyRequest request, CancellationToken ct) =>
        RunAsync(now => _state.Recoveries.TryGetValue(recoveryId, out var recovery)
            ? WithReplay($"verify:{recoveryId}:{idempotencyKey}", Hash(request.Code), () => CheckCode(recovery, request.Code, now))
            : MockResult.NotFound(), save: true, ct);

    public Task<MockResult> IssueResetLinkAsync(string recoveryId, ResetLinkRequest request, CancellationToken ct) =>
        RunAsync(now => IssueResetLink(recoveryId, request.OperationId, now), save: true, ct);

    public Task<MockResult> GetRecoveryAsync(string recoveryId, CancellationToken ct) =>
        RunAsync(now => _state.Recoveries.TryGetValue(recoveryId, out var recovery)
            ? MockResult.Json(200, recovery.ToView(now))
            : MockResult.NotFound(), save: false, ct);

    private async Task<T> RunAsync<T>(Func<DateTimeOffset, T> operation, bool save, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var result = operation(time.GetUtcNow());
            if (save)
            {
                // Not cancellable: the change is already in memory, so a dropped request must not skip the save.
                await store.WriteAsync(StateKey, _state, CancellationToken.None);
            }
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private MockResult StartRecovery(StartRecoveryRequest request, DateTimeOffset now)
    {
        var username = Normalize(request.Username);
        if (_state.Recoveries.Values.FirstOrDefault(r => r.RequestId == request.RequestId) is { } earlier)
        {
            return earlier.Username == username ? Accepted(earlier) : MockResult.IdempotencyConflict();
        }
        var blockedUntil = _state.Recoveries.Values
            .Where(r => r.Username == username)
            .Select(r => r.BlockedUntil())
            .DefaultIfEmpty(now)
            .Max();
        if (blockedUntil > now)
        {
            return MockResult.Throttled(blockedUntil - now);
        }
        // Unknown users get the same envelope and throttling, but no code is stored or delivered.
        var isKnown = FindUser(username) is not null;
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var recovery = new MockRecovery
        {
            Id = NewId("rec_"),
            Username = username,
            RequestId = request.RequestId,
            VerificationExpiresAt = now + s_codeLifetime,
            CodeHash = isKnown ? Hash(code) : null,
        };
        _state.Recoveries[recovery.Id] = recovery;
        if (isKnown)
        {
            Deliver(username, now, "Your verification code", $"Your verification code is {code}. It expires in 2 minutes.", link: null);
        }
        return Accepted(recovery);
    }

    private static MockResult CheckCode(MockRecovery recovery, string code, DateTimeOffset now) => recovery.StatusAt(now) switch
    {
        RecoveryStatus.Expired => MockResult.VerifyFail(
            410, "recovery_expired", "The verification window has ended.", recovery.AttemptsRemaining, RecoveryStatus.Expired),
        RecoveryStatus.Exhausted => Exhausted(),
        RecoveryStatus.AwaitingVerification => Attempt(recovery, code),
        _ => MockResult.InvalidState(), // already verified: a new attempt is refused
    };

    private static MockResult Attempt(MockRecovery recovery, string code)
    {
        if (recovery.CodeHash is { } expected && SecretEquals(Hash(code), expected))
        {
            recovery.Status = RecoveryStatus.Verified;
            recovery.CodeHash = null;
            return MockResult.Json(200, new VerifyResponse(recovery.Id, recovery.Status, recovery.AttemptsRemaining));
        }
        if (--recovery.AttemptsRemaining > 0)
        {
            return MockResult.VerifyFail(
                422, "verification_failed", "The code is not correct.", recovery.AttemptsRemaining, recovery.Status);
        }
        recovery.Status = RecoveryStatus.Exhausted;
        recovery.CodeHash = null;
        return Exhausted();
    }

    private static MockResult Exhausted() =>
        MockResult.VerifyFail(409, "verification_exhausted", "No verification attempts remain.", 0, RecoveryStatus.Exhausted);

    // One link per recovery: the same operation gets the same result, another operation is refused.
    private MockResult IssueResetLink(string recoveryId, string operationId, DateTimeOffset now)
    {
        if (!_state.Recoveries.TryGetValue(recoveryId, out var recovery))
        {
            return MockResult.NotFound();
        }
        if (recovery.LinkOperationId is { } issuedFor)
        {
            return issuedFor == operationId ? LinkIssued(recovery) : MockResult.InvalidState();
        }
        return recovery.StatusAt(now) switch
        {
            RecoveryStatus.Expired => MockResult.Fail(410, "recovery_expired", "The verification window has ended."),
            RecoveryStatus.Verified => SendLink(recovery, operationId, now),
            _ => MockResult.InvalidState(),
        };
    }

    private MockResult SendLink(MockRecovery recovery, string operationId, DateTimeOffset now)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        recovery.Status = RecoveryStatus.LinkIssued;
        recovery.LinkOperationId = operationId;
        recovery.TokenHash = Hash(token);
        recovery.LinkExpiresAt = now + s_linkLifetime;
        Deliver(
            recovery.Username,
            now,
            "Your password reset link",
            "Open the link to choose a new password. It works once and expires in 10 minutes.",
            $"{options.Value.ResetBaseUrl}#token={Uri.EscapeDataString(token)}");
        return LinkIssued(recovery);
    }

    // Same key + same request -> the recorded result; same key + different request -> 409 idempotency_conflict.
    private MockResult WithReplay(string key, string requestHash, Func<MockResult> operation)
    {
        if (_state.Replays.TryGetValue(key, out var replay))
        {
            return replay.RequestHash == requestHash ? replay.Result : MockResult.IdempotencyConflict();
        }
        var result = operation();
        _state.Replays[key] = new Replay(requestHash, result);
        return result;
    }

    private void Deliver(string username, DateTimeOffset now, string subject, string body, string? link) =>
        _state.Inbox.Add(new InboxMessage(NewId("msg_"), username, now, subject, body, link));

    private static MockResult Accepted(MockRecovery recovery) => MockResult.Json(
        202,
        new RecoveryAccepted(recovery.Id, RecoveryStatus.AwaitingVerification, recovery.VerificationExpiresAt, MockRecovery.MaxAttempts));

    private static MockResult LinkIssued(MockRecovery recovery) =>
        MockResult.Json(200, new ResetLinkResponse(recovery.Id, RecoveryStatus.LinkIssued, recovery.LinkExpiresAt));

    private static string NewId(string prefix) => prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
