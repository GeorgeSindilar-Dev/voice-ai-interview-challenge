# T3: Mock Issuer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The recovery API under `/mock/v1/recoveries` (start, verify, reset link, status) with the contract's rules,
inbox messages for code and link, and state that survives restarts.

**Architecture:** `MockIssuer` (singleton) owns one `MockState`, runs every operation under one `SemaphoreSlim`, saves
it to `IJsonStore` key `mock/state.json` after each mutation and is loaded before the server starts. Operations return
`MockResult` (status + snake_case JSON body, an `IResult`): expected failures are values. A filter checks
`Authorization: Bearer <Mock:ServiceCredential>` in constant time.

**Tech Stack:** .NET 10 minimal APIs, System.Text.Json, xUnit v3, `WebApplicationFactory<Program>`, `FakeTimeProvider`.
Needs T2. Commands run from `solution/`. Code blocks omit `using` directives; add the ones the compiler asks for.

**Files:** `src/VoiceReset/Mock/`: `MockOptions.cs`, `MockState.cs`, `MockContracts.cs`, `MockJson.cs`, `MockResult.cs`,
`MockIssuer.cs` (`partial`; T4 adds `MockIssuer.Resets.cs`), `MockIssuerEndpoints.cs`; modify `Program.cs`,
`appsettings.Development.json`. Tests in `tests/VoiceReset.Tests/Mock/`: `MockAppFactory.cs` (reused by T4/T5),
`MockIssuerEndpointsTests.cs`.

### Task 1: Development users, test host, failing tests
- [ ] **Step 1:** In `src/VoiceReset/appsettings.Development.json` add a `"Mock"` section (fake values; tests use them,
  Azure uses `Mock__...` app settings): `ServiceCredential` `"dev-only-service-credential"`, `ResetBaseUrl`
  `"http://localhost:5000/reset/"`, `Users`: `alice` (DisplayName `Alice Example`, InboxPassword `dev-only-inbox-alice`,
  InitialPassword `Dev-only-Alice-1`, RequiresUnlock `false`) and `bob` (`Bob Example`, `dev-only-inbox-bob`,
  `Dev-only-Bob-1`, `true`).
- [ ] **Step 2:** `tests/VoiceReset.Tests/Mock/MockAppFactory.cs`
```csharp
namespace VoiceReset.Tests.Mock;

public sealed class MockAppFactory : WebApplicationFactory<Program> // one app per test, Development settings, fake clock
{
    public const string ServiceCredential = "dev-only-service-credential"; // appsettings.Development.json
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public MockIssuer Issuer => Services.GetRequiredService<MockIssuer>();
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceCredential);
        return client;
    }
    public async Task<(string RecoveryId, string Code)> StartAsync(HttpClient client, string username, CancellationToken ct)
    {
        var reply = await PostAsync(client, "/mock/v1/recoveries", new { username, request_id = $"req-{username}" }, ct);
        var message = (await Issuer.GetInboxAsync(username, ct))[0];
        return (reply.Body.GetProperty("recovery_id").GetString() ?? "", Regex.Match(message.Body, @"\d{6}").Value);
    }
    public static Task<Reply> PostAsync(HttpClient client, string url, object body, CancellationToken ct) => SendAsync(client, url, body, null, ct);
    public static Task<Reply> VerifyAsync(HttpClient client, string recoveryId, string code, string key, CancellationToken ct) =>
        SendAsync(client, $"/mock/v1/recoveries/{recoveryId}/verify", new { code }, key, ct);
    public static string WrongCode(string code) => code == "000000" ? "111111" : "000000";
    private static async Task<Reply> SendAsync(HttpClient client, string url, object body, string? idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey); // ignored by routes other than verify
        using var response = await client.SendAsync(request, ct);
        return new Reply(response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(ct));
    }
}

public sealed record Reply(HttpStatusCode Status, JsonElement Body)
{
    public string? Error => Body.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;
    public int AttemptsRemaining => Body.GetProperty("attempts_remaining").GetInt32();
}
```
- [ ] **Step 3:** `tests/VoiceReset.Tests/Mock/MockIssuerEndpointsTests.cs`
```csharp
namespace VoiceReset.Tests.Mock;

public sealed class MockIssuerEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartRecovery_WithoutCredential_Returns401()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateClient();
        var reply = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "alice", request_id = "r1" }, Ct);

        Assert.Equal((HttpStatusCode.Unauthorized, "unauthenticated"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task StartRecovery_UnknownUser_ReturnsSameEnvelopeAndDeliversNothing()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var known = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "alice", request_id = "r1" }, Ct);
        var unknown = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "nobody", request_id = "r2" }, Ct);

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (known.Status, unknown.Status));
        Assert.Equal(known.Body.EnumerateObject().Select(p => p.Name), unknown.Body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(known.Body.GetProperty("verification_expires_at").GetString(), unknown.Body.GetProperty("verification_expires_at").GetString());
        Assert.Single(await app.Issuer.GetInboxAsync("alice", Ct));
        Assert.Empty(await app.Issuer.GetInboxAsync("nobody", Ct));
    }

    [Fact]
    public async Task Verify_After120Seconds_ReturnsRecoveryExpired()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alice", Ct);
        app.Clock.Advance(TimeSpan.FromSeconds(120));
        var reply = await MockAppFactory.VerifyAsync(client, recoveryId, code, "k1", Ct);

        Assert.Equal((HttpStatusCode.Gone, "recovery_expired"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task Verify_TwoWrongCodes_ExhaustsRecovery()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alice", Ct);
        var first = await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k1", Ct);
        var second = await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k2", Ct);
        var correctAfterwards = await MockAppFactory.VerifyAsync(client, recoveryId, code, "k3", Ct);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, 1), (first.Status, first.AttemptsRemaining));
        Assert.Equal((HttpStatusCode.Conflict, "verification_exhausted", 0), (second.Status, second.Error, second.AttemptsRemaining));
        Assert.Equal("exhausted", second.Body.GetProperty("status").GetString());
        Assert.Equal("verification_exhausted", correctAfterwards.Error);
    }

    [Fact]
    public async Task Verify_SameIdempotencyKeyTwice_CountsOneAttempt()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alice", Ct);
        await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k1", Ct);
        var replay = await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k1", Ct);
        var correct = await MockAppFactory.VerifyAsync(client, recoveryId, code, "k2", Ct);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, 1), (replay.Status, replay.AttemptsRemaining));
        Assert.Equal(HttpStatusCode.OK, correct.Status);
    }

    [Fact]
    public async Task ResetLink_BeforeVerification_ReturnsInvalidState()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, _) = await app.StartAsync(client, "alice", Ct);
        var reply = await MockAppFactory.PostAsync(client, $"/mock/v1/recoveries/{recoveryId}/reset-link", new { operation_id = "op-1" }, Ct);

        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (reply.Status, reply.Error));
        Assert.Single(await app.Issuer.GetInboxAsync("alice", Ct)); // the code only, no link
    }
}
```
- [ ] **Step 4:** `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Mock.MockIssuerEndpointsTests"` → build error CS0246 (`MockIssuer` not found).

### Task 2: Supporting types (`src/VoiceReset/Mock/`)
- [ ] **Step 1: `MockOptions.cs`** — sealed classes with `{ get; set; }`: `MockOptions` (`const SectionName = "Mock"`,
  `string ServiceCredential = ""`, `string ResetBaseUrl = ""`, `List<MockUser> Users = []`) and `MockUser` (`Username`,
  `DisplayName`, `InboxPassword`, `InitialPassword` all `= ""`, `bool RequiresUnlock`). `public static bool IsValid(MockOptions o)`
  (one expression): credential ≥ 16 chars, `Uri.TryCreate(o.ResetBaseUrl, UriKind.Absolute, out _)`, at least one user,
  every user has non-blank `Username`/`DisplayName` and non-empty passwords, usernames unique after `MockIssuer.Normalize`.
- [ ] **Step 2: `MockState.cs`** (everything saved in `mock/state.json`). Data holders, all `{ get; set; }`:
  - `MockState`: `Dictionary<string, MockRecovery> Recoveries = []`, `List<InboxMessage> Inbox = []`,
    `Dictionary<string, Replay> Replays = []` (key `"verify:{recoveryId}:{idempotencyKey}"`; T4 adds scopes).
  - `record InboxMessage(string Id, string Username, DateTimeOffset CreatedAt, string Subject, string Body, string? Link)`.
  - `record Replay(string RequestHash, MockResult Result)` — the recorded result and a hash of the request.
  - `static class RecoveryStatus`: string consts `AwaitingVerification = "awaiting_verification"`, `Verified`,
    `LinkIssued`, `Completed`, `Exhausted`, `Expired` (value = snake_case name).
  - `MockRecovery` (codes/tokens only as SHA-256 hex): `const int MaxAttempts = 2`; `required ... { get; init; }`
    `string Id`, `string Username` (normalized), `string RequestId`, `DateTimeOffset VerificationExpiresAt`; settable
    `string? CodeHash` (null for unknown users and once used), `string Status = RecoveryStatus.AwaitingVerification`,
    `int AttemptsRemaining = MaxAttempts`, `string? LinkOperationId`, `string? TokenHash`, `DateTimeOffset? LinkExpiresAt`,
    and (set by T4) `string? ResetOperationId`, `string? ResetReceipt`, `string? UnlockStatus`. Plus `ToView(now)` =
    `new RecoveryView(Id, StatusAt(now), VerificationExpiresAt, AttemptsRemaining, LinkExpiresAt, ResetOperationId, ResetReceipt, UnlockStatus)` and:
```csharp
    public string StatusAt(DateTimeOffset now) => Status switch
    {
        RecoveryStatus.AwaitingVerification or RecoveryStatus.Verified or RecoveryStatus.Exhausted
            when now >= VerificationExpiresAt => RecoveryStatus.Expired,
        RecoveryStatus.LinkIssued when now >= LinkExpiresAt => RecoveryStatus.Expired,
        _ => Status,
    };
    /// <summary>No new recovery for the account before this: the 120 s code window, or link expiry while a link is out.</summary>
    public DateTimeOffset BlockedUntil() =>
        Status == RecoveryStatus.LinkIssued && LinkExpiresAt is { } linkExpiry ? linkExpiry : VerificationExpiresAt;
```
- [ ] **Step 3: `MockContracts.cs`** (snake_case on the wire via `MockJson.Options`)
```csharp
namespace VoiceReset.Mock;

public sealed record StartRecoveryRequest(string Username, string RequestId);
public sealed record VerifyRequest(string Code);
public sealed record ResetLinkRequest(string OperationId);
public sealed record RecoveryAccepted(string RecoveryId, string Status, DateTimeOffset VerificationExpiresAt, int AttemptsRemaining);
public sealed record VerifyResponse(string RecoveryId, string Status, int AttemptsRemaining);
public sealed record ResetLinkResponse(string RecoveryId, string Status, DateTimeOffset? LinkExpiresAt);
public sealed record RecoveryView(string RecoveryId, string Status, DateTimeOffset VerificationExpiresAt, int AttemptsRemaining,
    DateTimeOffset? LinkExpiresAt, string? ResetOperationId, string? ResetReceipt, string? UnlockStatus);
```
- [ ] **Step 4: `MockJson.cs`** — `public static class MockJson` with
  `public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { ... }` setting
  `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`, `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`,
  `NumberHandling = JsonNumberHandling.Strict`, `RespectNullableAnnotations = true`, `RespectRequiredConstructorParameters = true`; and
  `public static async Task<T?> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class` that returns
  `request.HasJsonContentType() ? await request.ReadFromJsonAsync<T>(Options, ct) : null` inside
  `try { ... } catch (JsonException) { return null; }` (null = invalid JSON, wrong type, missing or unknown field).
- [ ] **Step 5: `MockResult.cs`** — static helpers (each a one-line expression) building error bodies
  `{"error":{"code","message"}}` with fixed messages: `InvalidRequest()` 400 `invalid_request` "The request is not
  valid."; `Unauthenticated()` 401 `unauthenticated` "Authentication is required."; `NotFound()` 404 `not_found` "The
  resource was not found."; `InvalidState()` 409 `invalid_state` "The operation is not allowed in the current state.";
  `IdempotencyConflict()` 409 `idempotency_conflict` "The key was used for a different request."; `Throttled(retryAfter)`
  429 `throttled` "Too many requests. Try again later." `with { RetryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds) }`.
```csharp
namespace VoiceReset.Mock;

// Body is a snake_case JsonElement, so it is stored as-is in Replays (the store's camelCase options don't touch it).
public sealed record MockResult(int StatusCode, JsonElement Body, int? RetryAfterSeconds = null) : IResult
{
    public static MockResult Json(int statusCode, object body) => new(statusCode, JsonSerializer.SerializeToElement(body, MockJson.Options));
    public static MockResult Fail(int statusCode, string code, string message) => Json(statusCode, new ErrorBody(new(code, message)));
    public static MockResult VerifyFail(int statusCode, string code, string message, int attemptsRemaining, string status) =>
        Json(statusCode, new VerifyErrorBody(new(code, message), attemptsRemaining, status));

    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (RetryAfterSeconds is { } seconds)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }
        httpContext.Response.StatusCode = StatusCode;
        return httpContext.Response.WriteAsJsonAsync(Body, MockJson.Options, httpContext.RequestAborted);
    }

    private sealed record ErrorDetail(string Code, string Message);
    private sealed record ErrorBody(ErrorDetail Error);
    private sealed record VerifyErrorBody(ErrorDetail Error, int AttemptsRemaining, string Status);
}
```

### Task 3: Issuer, endpoints, wiring
- [ ] **Step 1: `src/VoiceReset/Mock/MockIssuer.cs`**
```csharp
namespace VoiceReset.Mock;

public sealed partial class MockIssuer(IJsonStore store, IOptions<MockOptions> options, TimeProvider time) : IDisposable
{
    public const string StateKey = "mock/state.json";
    private static readonly TimeSpan s_codeLifetime = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan s_linkLifetime = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private MockState _state = new();
    public static string Normalize(string username) => username.Trim().ToLowerInvariant();
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool SecretEquals(string a, string b) => // constant time; hashing first makes the lengths equal
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    public string ServiceCredential => options.Value.ServiceCredential;
    public MockUser? FindUser(string username) => options.Value.Users.FirstOrDefault(u => Normalize(u.Username) == Normalize(username));
    public async Task LoadAsync(CancellationToken ct) => _state = await store.ReadAsync<MockState>(StateKey, ct) ?? new();
    public void Dispose() => _lock.Dispose();
    public Task<IReadOnlyList<InboxMessage>> GetInboxAsync(string username, CancellationToken ct) => RunAsync<IReadOnlyList<InboxMessage>>(
        _ => [.. _state.Inbox.Where(m => m.Username == Normalize(username)).OrderByDescending(m => m.CreatedAt)], save: false, ct);
    public Task<MockResult> StartRecoveryAsync(StartRecoveryRequest request, CancellationToken ct) =>
        RunAsync(now => StartRecovery(request, now), save: true, ct);
    public Task<MockResult> VerifyAsync(string recoveryId, string idempotencyKey, VerifyRequest request, CancellationToken ct) =>
        RunAsync(now => _state.Recoveries.TryGetValue(recoveryId, out var recovery)
            ? WithReplay($"verify:{recoveryId}:{idempotencyKey}", Hash(request.Code), () => CheckCode(recovery, request.Code, now))
            : MockResult.NotFound(), save: true, ct);
    public Task<MockResult> IssueResetLinkAsync(string recoveryId, ResetLinkRequest request, CancellationToken ct) =>
        RunAsync(now => IssueResetLink(recoveryId, request.OperationId, now), save: true, ct);
    public Task<MockResult> GetRecoveryAsync(string recoveryId, CancellationToken ct) => RunAsync(now =>
        _state.Recoveries.TryGetValue(recoveryId, out var r) ? MockResult.Json(200, r.ToView(now)) : MockResult.NotFound(), save: false, ct);

    private async Task<T> RunAsync<T>(Func<DateTimeOffset, T> operation, bool save, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var result = operation(time.GetUtcNow());
            if (save)
            {
                await store.WriteAsync(StateKey, _state, ct);
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
        var blockedUntil = _state.Recoveries.Values.Where(r => r.Username == username).Select(r => r.BlockedUntil()).DefaultIfEmpty(now).Max();
        if (blockedUntil > now)
        {
            return MockResult.Throttled(blockedUntil - now);
        }
        // Unknown users get the same envelope and throttling, but no code is stored or delivered.
        var isKnown = FindUser(username) is not null;
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var recovery = new MockRecovery { Id = NewId("rec_"), Username = username, RequestId = request.RequestId,
            VerificationExpiresAt = now + s_codeLifetime, CodeHash = isKnown ? Hash(code) : null };
        _state.Recoveries[recovery.Id] = recovery;
        if (isKnown)
        {
            Deliver(username, now, "Your verification code", $"Your verification code is {code}. It expires in 2 minutes.", link: null);
        }
        return Accepted(recovery);
    }
    private static MockResult CheckCode(MockRecovery recovery, string code, DateTimeOffset now) => recovery.StatusAt(now) switch
    {
        RecoveryStatus.Expired => MockResult.VerifyFail(410, "recovery_expired", "The verification window has ended.", recovery.AttemptsRemaining, RecoveryStatus.Expired),
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
            return MockResult.VerifyFail(422, "verification_failed", "The code is not correct.", recovery.AttemptsRemaining, recovery.Status);
        }
        recovery.Status = RecoveryStatus.Exhausted;
        recovery.CodeHash = null;
        return Exhausted();
    }
    private static MockResult Exhausted() =>
        MockResult.VerifyFail(409, "verification_exhausted", "No verification attempts remain.", 0, RecoveryStatus.Exhausted);

    // One link per recovery: the same operation gets the same result, another operation is refused.
    private MockResult IssueResetLink(string recoveryId, string operationId, DateTimeOffset now) =>
        !_state.Recoveries.TryGetValue(recoveryId, out var recovery) ? MockResult.NotFound()
        : recovery.LinkOperationId is { } issuedFor ? (issuedFor == operationId ? LinkIssued(recovery) : MockResult.InvalidState())
        : recovery.StatusAt(now) switch
        {
            RecoveryStatus.Expired => MockResult.Fail(410, "recovery_expired", "The verification window has ended."),
            RecoveryStatus.Verified => SendLink(recovery, operationId, now),
            _ => MockResult.InvalidState(),
        };

    private MockResult SendLink(MockRecovery recovery, string operationId, DateTimeOffset now)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        recovery.Status = RecoveryStatus.LinkIssued;
        recovery.LinkOperationId = operationId;
        recovery.TokenHash = Hash(token);
        recovery.LinkExpiresAt = now + s_linkLifetime;
        Deliver(recovery.Username, now, "Your password reset link", "Open the link to choose a new password. It works once and expires in 10 minutes.",
            $"{options.Value.ResetBaseUrl}#token={Uri.EscapeDataString(token)}");
        return LinkIssued(recovery);
    }
    // Same key + same request → the recorded result; same key + different request → 409 idempotency_conflict.
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
    private static MockResult Accepted(MockRecovery r) =>
        MockResult.Json(202, new RecoveryAccepted(r.Id, RecoveryStatus.AwaitingVerification, r.VerificationExpiresAt, MockRecovery.MaxAttempts));
    private static MockResult LinkIssued(MockRecovery r) => MockResult.Json(200, new ResetLinkResponse(r.Id, RecoveryStatus.LinkIssued, r.LinkExpiresAt));
    private static string NewId(string prefix) => prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
```
- [ ] **Step 2: `src/VoiceReset/Mock/MockIssuerEndpoints.cs`**
```csharp
namespace VoiceReset.Mock;

public static class MockIssuerEndpoints
{
    public static IEndpointRouteBuilder MapMockIssuer(this IEndpointRouteBuilder endpoints)
    {
        var recoveries = endpoints.MapGroup("/mock/v1/recoveries").AddEndpointFilter(RequireServiceCredentialAsync);
        recoveries.MapPost("", async (HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
            await MockJson.ReadAsync<StartRecoveryRequest>(request, ct) is { } body ? await issuer.StartRecoveryAsync(body, ct) : MockResult.InvalidRequest());
        recoveries.MapPost("/{id}/verify", VerifyAsync);
        recoveries.MapPost("/{id}/reset-link", async (string id, HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
            await MockJson.ReadAsync<ResetLinkRequest>(request, ct) is { } body ? await issuer.IssueResetLinkAsync(id, body, ct) : MockResult.InvalidRequest());
        recoveries.MapGet("/{id}", (string id, MockIssuer issuer, CancellationToken ct) => issuer.GetRecoveryAsync(id, ct));
        return endpoints;
    }

    public static async ValueTask<object?> RequireServiceCredentialAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var expected = $"Bearer {http.RequestServices.GetRequiredService<MockIssuer>().ServiceCredential}";
        return MockIssuer.SecretEquals(http.Request.Headers.Authorization.ToString(), expected) ? await next(context) : MockResult.Unauthenticated();
    }

    // A malformed request (bad JSON, blank code, no key) is not a completed submission: it never counts as an attempt.
    private static async Task<MockResult> VerifyAsync(string id, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<VerifyRequest>(request, ct) is { } body && !string.IsNullOrWhiteSpace(body.Code) && !string.IsNullOrWhiteSpace(idempotencyKey)
            ? await issuer.VerifyAsync(id, idempotencyKey, body, ct)
            : MockResult.InvalidRequest();
}
```
- [ ] **Step 3: Wire up `src/VoiceReset/Program.cs`.** After `AddJsonStore(...)`:
  `builder.Services.AddOptions<MockOptions>().Bind(builder.Configuration.GetSection(MockOptions.SectionName)).Validate(MockOptions.IsValid, "Mock settings are missing or invalid.").ValidateOnStart();`
  and `builder.Services.AddSingleton<MockIssuer>();`. After `app.MapHealth();` add `app.MapMockIssuer();`; just before
  `await app.RunAsync();` add `await app.Services.GetRequiredService<MockIssuer>().LoadAsync(app.Lifetime.ApplicationStopping);`

- [ ] **Step 4:** Rerun the filtered tests → `Test run summary: Passed!`, total 6, failed 0. Then
  `dotnet build VoiceReset.slnx -c Release` → 0 warnings, and `dotnet test --project tests/VoiceReset.Tests` → total 13, failed 0.
- [ ] **Step 5:** `git add src/VoiceReset tests/VoiceReset.Tests` and `git commit -m "feat(mock): add recovery, verify and reset-link endpoints"`

## Self-review

Each rule maps to code and, where asked, a test: 401 (test 1), unknown user (test 2), 120 s (test 3), exhaustion (test 4),
replay (test 5), link before verify (test 6); 429 + `Retry-After` (`BlockedUntil`), 410, `invalid_state` after verify,
one link per recovery, persistence. Analyzer traps handled: IDE0011 braces, CA1001, CA1305, CA1068.

## Questions

1. Code/token hashes are plain SHA-256 in `mock/state.json`; a 6-digit code is brute-forceable from the file. OK for a mock?
2. A failed blob write leaves memory ahead of storage until the next write (the caller gets 500). OK?
3. `ResetBaseUrl` accepts `http://` for local runs. Require `https://` outside Development?
4. Blank `username`/`request_id`/`operation_id` are accepted as opaque strings (only a blank code or key is refused);
   tests reuse the fake users in `appsettings.Development.json`. Both OK?

## Additions to contracts

- `MockState.Replays` + `Replay`; `MockRecovery` (`StatusAt`, `BlockedUntil`, `MaxAttempts`); `RecoveryStatus`; `MockContracts.cs`
  records; `MockResult : IResult` + helpers; `MockJson.ReadAsync<T>`; `MockIssuerEndpoints.RequireServiceCredentialAsync`.
- `MockIssuer` (`partial`): `Normalize`, `Hash`, `SecretEquals`, `ServiceCredential`, `FindUser`, `LoadAsync`, `GetInboxAsync`
  (newest first), the four operations; private `RunAsync`, `WithReplay`, `Deliver`, `NewId` (used by T4).
- Dev users `alice`, `bob` (`RequiresUnlock`). Tests: `MockAppFactory` + `Reply` (used by T4, T5).
