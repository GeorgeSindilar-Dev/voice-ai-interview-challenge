# T6 Recovery Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The backend state machine behind the voice tools: it calls the mock issuer over HTTP, saves each call's state and returns the exact sentence the agent may say.

**Architecture:** `RecoveryWorkflow` (singleton) runs each tool under a per-session lock: load the `CallSession`, check the state, save new IDs **before** calling the issuer, map the result to a `ToolResult` with a fixed `Phrases` sentence, save. `IssuerClient` is a typed `HttpClient`; timeout, 5xx or network error = `Unavailable`, never success. **Tech Stack:** .NET 10, `IHttpClientFactory`, System.Text.Json, xUnit v3, `WebApplicationFactory<Program>` + in-process mock (T3/T4), `FakeTimeProvider`.

Rules: CLAUDE.md 1, 3 (backend decides; truth from the backend; same words for every username), 4 (no codes in logs). Names from `00-contracts.md`. Source: `solution/src/VoiceReset/Recovery/` (namespace `VoiceReset.Recovery`); tests: `solution/tests/VoiceReset.Tests/Recovery/` (`VoiceReset.Tests.Recovery`). Code blocks omit `namespace`/`using` lines (add what the compiler asks for, e.g. `Microsoft.AspNetCore.TestHost`, `Microsoft.AspNetCore.Hosting.Server`, `Microsoft.Extensions.Time.Testing`, `System.Text.Json.Nodes`). Commands run from `solution/`.

---

### Task 1: Supporting types

- [ ] **Step 1: Plain types** (no logic; write directly)
  - `IssuerOptions`: `const string Section = "Issuer"`; `[Required, Url] string BaseUrl = ""` (ends with `/`, e.g. `https://host/mock/`); `[Required] string ServiceCredential = ""`.
  - `CallSession.cs`: `[JsonConverter(typeof(JsonStringEnumConverter<RecoveryState>))] public enum RecoveryState { AwaitingUsername, AwaitingCode, Verified, LinkSent, Completed, Escalated, Cancelled }`; `public sealed class CallSession` with `required init` `string SessionId`, `string Channel`, `DateTimeOffset StartedAt`; `get; set;` `RecoveryState State`, `DateTimeOffset? EndedAt` and `string?` `Username`, `RequestId`, `RecoveryId`, `VerifyKey`, `LinkOperationId`, `TicketOperationId`, `TicketId`, `OutcomeOperationId`, `TicketOutcome`, `TicketReason`, `ResetReceipt`, `UnlockStatus`, `EndReason`; `[JsonIgnore] public bool IsOpen => State is AwaitingUsername or AwaitingCode or Verified or LinkSent;`. Never a code or token.
  - `SessionStore(IJsonStore store)`: key `sessions/<id>.json`; `GetAsync(id, ct)`, `SaveAsync(session, ct)`, `ListOpenAsync(ct)` (= `ListKeysAsync("sessions/")`, read each, keep `IsOpen`; returns `IReadOnlyList<CallSession>`).

- [ ] **Step 2: `ToolResult.cs`** (the only sentences the agent may say about the reset)
```csharp
/// <summary>Result of a tool call. Say is the exact sentence the agent must use.</summary>
public sealed record ToolResult(bool Ok, string Status, string Say);

public static class Phrases
{
    public const string CodeSent = "If that account is enrolled, a verification code has been sent to its recovery inbox. It's valid for two minutes.";
    public const string UsernameUnclear = "I didn't catch a valid username. Please spell it out, for example: alex dot morgan.";
    public const string CantStartNow = "I can't start a reset for that username right now. Please try again in a few minutes.";
    public const string CodeUnclear = "I need all six digits of the code. Please read the whole code again.";
    public const string Verified = "Thanks, the code is verified. I can now send a password reset link to the same recovery inbox.";
    public const string CodeIncorrect = "That code wasn't correct. You have one try left.";
    public const string Exhausted = "That code wasn't correct either, so this reset attempt is now locked.";
    public const string Expired = "The verification code has expired, so I can't continue this reset.";
    public const string LinkSent = "I've sent a password reset link to the same recovery inbox. It's valid for 10 minutes, and it stays valid until it expires. Open it and choose your new password in the form; please don't tell me the password.";
    public const string Completed = "Your password has been reset.";
    public const string CompletedWithUnlock = "Your password has been reset and your account is unlocked.";
    public const string CantConfirmYet = "I can't confirm the reset yet. When you've submitted the form, ask me to check again.";
    public const string ResetFailed = "The reset didn't complete.";
    public const string LinkExpired = "The reset link expired before a reset was completed.";
    public const string HumanRequested = "I can't transfer you to a person.";
    public const string TicketCreated = "A help-desk ticket was created; no one has joined this call.";
    public const string TicketNotCreated = "I couldn't create a help-desk ticket, and no one has joined this call. Please contact your help desk directly.";
    public const string Cancelled = "OK, I've cancelled this reset.";
    public const string CancelledAfterLink = "OK, I've stopped here. The link already sent can't be withdrawn; it stays valid until it expires.";
    public const string NotAvailableNow = "The reset service isn't responding right now. Please try again in a moment.";
    public const string NotAllowed = "I can't do that at this step of the reset.";
}
```

- [ ] **Step 3: `SpokenInput.cs`**
```csharp
/// <summary>Speech-to-text → username or 6-digit code; null when unclear (never guessed).</summary>
public static partial class SpokenInput
{
    private static readonly Dictionary<string, string> s_symbols = new() { ["dot"] = ".", ["period"] = ".", ["underscore"] = "_", ["dash"] = "-", ["hyphen"] = "-" };
    private static readonly Dictionary<string, string> s_digits = new() { ["zero"] = "0", ["oh"] = "0", ["one"] = "1", ["two"] = "2",
        ["three"] = "3", ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9" };
    /// <summary>"Alex dot Morgan" → "alex.morgan".</summary>
    public static string? Username(string? spoken) => Normalize(spoken, UsernameSeparators(), s_symbols, UsernamePattern());
    /// <summary>"oh 4 7-1 one two" → "047112"; anything that isn't exactly 6 digits (e.g. "4 7") → null.</summary>
    public static string? Code(string? spoken) => Normalize(spoken, CodeSeparators(), s_digits, CodePattern());
    private static string? Normalize(string? spoken, Regex separators, Dictionary<string, string> words, Regex valid)
    {
        var text = string.Concat(separators.Split((spoken ?? "").Trim().ToLowerInvariant()).Select(w => words.GetValueOrDefault(w, w)));
        return valid.IsMatch(text) ? text : null;
    }
    [GeneratedRegex(@"[\s,]+")] private static partial Regex UsernameSeparators();
    [GeneratedRegex(@"[\s,.\-]+")] private static partial Regex CodeSeparators();
    [GeneratedRegex("^[a-z0-9._-]{3,64}$")] private static partial Regex UsernamePattern();
    [GeneratedRegex("^[0-9]{6}$")] private static partial Regex CodePattern();
}
```

- [ ] **Step 4: `IssuerClient.cs`** — `IssuerClient(HttpClient http)`; `s_json` = `new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }`. DTO records (only the fields we use; others are ignored): `RecoveryStarted(string RecoveryId, string Status)`, `VerifyResult(string RecoveryId, string Status)`, `LinkIssued(string RecoveryId, string Status, DateTimeOffset LinkExpiresAt)`, `RecoveryStatus(string RecoveryId, string Status, string? ResetReceipt, string? UnlockStatus)`, `TicketInfo(string TicketId, string RecoveryId, string Outcome)`. Each public method is one line, `SendAsync<TDto>(method, path, body, idempotencyKey, ct)`; IDs in paths go through `Uri.EscapeDataString`; bodies are anonymous objects with snake_case names (`new { username, request_id = requestId }`).

| Method → result | Route | Body |
|---|---|---|
| `StartRecoveryAsync(username, requestId, ct)` → `RecoveryStarted` | `POST v1/recoveries` | `username, request_id` |
| `VerifyAsync(recoveryId, code, idempotencyKey, ct)` → `VerifyResult` | `POST v1/recoveries/{id}/verify` + `Idempotency-Key` | `code` |
| `SendResetLinkAsync(recoveryId, operationId, ct)` → `LinkIssued` | `POST v1/recoveries/{id}/reset-link` | `operation_id` |
| `GetRecoveryAsync(recoveryId, ct)` → `RecoveryStatus` | `GET v1/recoveries/{id}` | none |
| `CreateTicketAsync(recoveryId, operationId, ct)` → `TicketInfo` | `POST v1/tickets` | `recovery_id, operation_id` |
| `SetTicketOutcomeAsync(ticketId, outcome, receipt, reasonCode, operationId, ct)` → `TicketInfo` | `POST v1/tickets/{id}/outcome` | `outcome, reset_receipt` (may be null), `reason_code, operation_id` |
```csharp
public enum IssuerOutcome { Success, Error, Unavailable }
/// <summary>Success has a Value; Error has the issuer's error code; Unavailable = outcome unknown (timeout, 5xx, network).</summary>
public sealed record IssuerResult<T>(IssuerOutcome Outcome, T? Value, string? ErrorCode, int? AttemptsRemaining) where T : class;
// inside IssuerClient:
private async Task<IssuerResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    where T : class
{
    using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: s_json) };
    if (idempotencyKey is not null) { request.Headers.Add("Idempotency-Key", idempotencyKey); }
    try
    {
        using var response = await http.SendAsync(request, ct);
        if ((int)response.StatusCode >= 500) { return new(IssuerOutcome.Unavailable, null, null, null); }
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(s_json, ct);
            return new(value is null ? IssuerOutcome.Unavailable : IssuerOutcome.Success, value, null, null);
        }
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(s_json, ct);
        return new(IssuerOutcome.Error, null, error?.Error?.Code ?? "unknown", error?.AttemptsRemaining);
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
    {
        return new(IssuerOutcome.Unavailable, null, null, null);   // the issuer may or may not have acted
    }
}
private sealed record ErrorResponse(ErrorDetail? Error, int? AttemptsRemaining);
private sealed record ErrorDetail(string Code);
```

- [ ] **Step 5: Build, commit** — `dotnet build VoiceReset.slnx -c Release` → `Build succeeded`, `0 Warning(s)`, `0 Error(s)`; `git add solution/src/VoiceReset/Recovery && git commit -m "feat: add issuer client, call session and phrases"`

### Task 2: Failing tests for the workflow rules

- [ ] **Step 1: `RecoveryAppFactory.cs`** (real app + in-process mock; the issuer client goes through the test server; reused by T7)
```csharp
public sealed class RecoveryAppFactory(InMemoryJsonStore? store = null) : WebApplicationFactory<Program>
{
    public InMemoryJsonStore Store { get; } = store ?? new InMemoryJsonStore();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public RecoveryWorkflow Workflow => Services.GetRequiredService<RecoveryWorkflow>();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Mock:ServiceCredential", "test-credential");
        builder.UseSetting("Issuer:ServiceCredential", "test-credential");
        builder.UseSetting("Issuer:BaseUrl", "http://localhost/mock/");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IJsonStore>(Store);
            services.AddSingleton<TimeProvider>(Time);
            services.AddHttpClient<IssuerClient>().ConfigurePrimaryHttpMessageHandler(sp => ((TestServer)sp.GetRequiredService<IServer>()).CreateHandler());
        });
    }
    public async Task<string> StartRecoveryAsync(string spokenUsername, CancellationToken ct)
    {
        var id = await Workflow.StartSessionAsync("test", ct);
        await Workflow.StartRecoveryAsync(id, spokenUsername, ct);
        return id;
    }
    public Task<CallSession?> SessionAsync(string id, CancellationToken ct) => Services.GetRequiredService<SessionStore>().GetAsync(id, ct);
    // Test shortcut for the inbox page: the newest code / link in the mock's saved state (messages are appended).
    public async Task<string> InboxCodeAsync(CancellationToken ct) => Regex.Matches(await MockStateAsync(ct), @"""body"":""[^""]*?\b([0-9]{6})\b")[^1].Groups[1].Value;
    public async Task<string> InboxLinkAsync(CancellationToken ct) => Regex.Matches(await MockStateAsync(ct), @"""link"":""([^""]+)""")[^1].Groups[1].Value;
    private async Task<string> MockStateAsync(CancellationToken ct) => (await Store.ReadAsync<JsonObject>("mock/state.json", ct))?.ToJsonString() ?? "";
    // What the caller does in the browser form.
    public async Task CompleteResetAsync(string link, CancellationToken ct)
    {
        var token = Uri.UnescapeDataString(link[(link.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..]);
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/mock/v1/resets",
            new { token, new_password = "Blue-Harbor-Lantern-42", operation_id = Guid.NewGuid().ToString("N") }, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 2: `RecoveryWorkflowTests.cs`** (Arrange / Act / Assert separated by blank lines)
```csharp
public sealed class RecoveryWorkflowTests
{
    private const string Spoken = "alex dot morgan";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendResetLink_BeforeVerification_IsRefused()
    {
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);

        var result = await app.Workflow.SendResetLinkAsync(id, Ct);

        Assert.Equal(new ToolResult(false, "not_allowed", Phrases.NotAllowed), result);
        Assert.Equal(RecoveryState.AwaitingCode, await app.Workflow.GetStateAsync(id, Ct));
    }

    [Fact]
    public async Task CheckResetStatus_FullJourney_SaysSuccessOnlyWithReceipt()
    {
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);
        var spokenCode = string.Join(' ', (await app.InboxCodeAsync(Ct)).ToCharArray());   // "0 4 7 1 1 2"
        Assert.Equal("verified", (await app.Workflow.SubmitCodeAsync(id, spokenCode, Ct)).Status);
        Assert.Equal(Phrases.LinkSent, (await app.Workflow.SendResetLinkAsync(id, Ct)).Say);

        var beforeReset = await app.Workflow.CheckResetStatusAsync(id, Ct);
        await app.CompleteResetAsync(await app.InboxLinkAsync(Ct), Ct);
        var afterReset = await app.Workflow.CheckResetStatusAsync(id, Ct);

        Assert.Equal(Phrases.CantConfirmYet, beforeReset.Say);
        Assert.Equal(Phrases.Completed, afterReset.Say);
        var session = await app.SessionAsync(id, Ct);
        Assert.NotNull(session?.ResetReceipt);
        Assert.Equal("resolved", session?.TicketOutcome);
    }

    [Fact]
    public async Task SubmitCode_Fragment_IsNotSubmitted()
    {
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);

        var fragment = await app.Workflow.SubmitCodeAsync(id, "4 7", Ct);
        var wrong = await app.Workflow.SubmitCodeAsync(id, WrongCode(await app.InboxCodeAsync(Ct)), Ct);

        Assert.Equal(new ToolResult(false, "invalid_code", Phrases.CodeUnclear), fragment);
        Assert.Equal(Phrases.CodeIncorrect, wrong.Say);   // one try left: the fragment used no attempt
    }

    [Fact]
    public async Task SubmitCode_SecondWrongCode_EscalatesAsExhausted()
    {
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);
        var wrong = WrongCode(await app.InboxCodeAsync(Ct));
        await app.Workflow.SubmitCodeAsync(id, wrong, Ct);

        var second = await app.Workflow.SubmitCodeAsync(id, wrong, Ct);

        Assert.Equal(new ToolResult(false, "exhausted", $"{Phrases.Exhausted} {Phrases.TicketCreated}"), second);
        var session = await app.SessionAsync(id, Ct);
        Assert.Equal(RecoveryState.Escalated, session?.State);
        Assert.Equal("verification_exhausted", session?.TicketReason);
    }

    [Fact]
    public async Task StartRecovery_SecondSessionSameAccount_DoesNotShareRecovery()
    {
        await using var app = new RecoveryAppFactory();
        var first = await app.StartRecoveryAsync(Spoken, Ct);
        await app.Workflow.SubmitCodeAsync(first, await app.InboxCodeAsync(Ct), Ct);

        var second = await app.StartRecoveryAsync(Spoken, Ct);   // throttled by the issuer: no recovery
        var secondLink = await app.Workflow.SendResetLinkAsync(second, Ct);

        Assert.Equal(RecoveryState.AwaitingUsername, await app.Workflow.GetStateAsync(second, Ct));
        Assert.Equal("not_allowed", secondLink.Status);
        Assert.Equal(RecoveryState.Verified, await app.Workflow.GetStateAsync(first, Ct));
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";
}
```

- [ ] **Step 3: Run, expect a compile failure** — `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Recovery.RecoveryWorkflowTests"` → `error CS0246: The type or namespace name 'RecoveryWorkflow' could not be found`.

### Task 3: RecoveryWorkflow and wiring

- [ ] **Step 1: `RecoveryWorkflow.cs`** (`csharp_prefer_braces` is a build warning, so one-line guards keep `{ }`)
```csharp
/// <summary>The backend decides: each tool is checked against this call's state; outcomes come only from the issuer.</summary>
public sealed partial class RecoveryWorkflow(SessionStore sessions, IssuerClient issuer, TimeProvider time, ILogger<RecoveryWorkflow> logger)
{
    private static readonly ToolResult s_notAllowed = new(false, "not_allowed", Phrases.NotAllowed);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    public async Task<string> StartSessionAsync(string channel, CancellationToken ct)
    {
        var session = new CallSession { SessionId = NewId(), Channel = channel, StartedAt = time.GetUtcNow() };
        await sessions.SaveAsync(session, ct);
        return session.SessionId;
    }
    public async Task<RecoveryState> GetStateAsync(string sessionId, CancellationToken ct) => (await LoadAsync(sessionId, ct)).State;
    public Task<ToolResult> StartRecoveryAsync(string sessionId, string? spokenUsername, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session.State != RecoveryState.AwaitingUsername) { return s_notAllowed; }
        if (SpokenInput.Username(spokenUsername) is not { } username) { return new(false, "invalid_username", Phrases.UsernameUnclear); }
        if (session.RequestId is null || session.Username != username)
        {
            session.Username = username;
            session.RequestId = NewId();
            await sessions.SaveAsync(session, ct);
        }
        var result = await issuer.StartRecoveryAsync(username, session.RequestId, ct);
        if (result.Value is null)
        {
            if (result.Outcome == IssuerOutcome.Error) { session.RequestId = null; }   // after a timeout, keep it for a safe retry
            return new(false, "not_started", Phrases.CantStartNow);
        }
        session.RecoveryId = result.Value.RecoveryId;
        session.State = RecoveryState.AwaitingCode;
        await EnsureTicketAsync(session, ct);
        return new(true, "code_sent", Phrases.CodeSent);
    }, ct);
    public Task<ToolResult> SubmitCodeAsync(string sessionId, string? spokenCode, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session is not { State: RecoveryState.AwaitingCode, RecoveryId: { } recoveryId }) { return s_notAllowed; }
        if (SpokenInput.Code(spokenCode) is not { } code) { return new(false, "invalid_code", Phrases.CodeUnclear); }
        // Kept after a timeout so a retry of that submission can't count twice; any answer (also
        // idempotency_conflict when the caller now reads a different code) clears it for the next one.
        session.VerifyKey ??= NewId();
        await sessions.SaveAsync(session, ct);
        var result = await issuer.VerifyAsync(recoveryId, code, session.VerifyKey, ct);
        if (result.Outcome != IssuerOutcome.Unavailable) { session.VerifyKey = null; }
        if (result.Outcome == IssuerOutcome.Success)
        {
            session.State = RecoveryState.Verified;
            return new(true, "verified", Phrases.Verified);
        }
        return result.ErrorCode switch
        {
            "verification_failed" => new(false, "code_incorrect", Phrases.CodeIncorrect),
            "verification_exhausted" => await EscalateAsync(session, "verification_exhausted", false, "exhausted", Phrases.Exhausted, ct),
            "recovery_expired" => await EscalateAsync(session, "verification_expired", false, "expired", Phrases.Expired, ct),
            _ => new(false, "unavailable", Phrases.NotAvailableNow),
        };
    }, ct);
    public Task<ToolResult> SendResetLinkAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session is not { State: RecoveryState.Verified, RecoveryId: { } recoveryId }) { return s_notAllowed; }
        session.LinkOperationId ??= NewId();
        await sessions.SaveAsync(session, ct);
        var result = await issuer.SendResetLinkAsync(recoveryId, session.LinkOperationId, ct);
        if (result.ErrorCode == "recovery_expired") { return await EscalateAsync(session, "verification_expired", false, "expired", Phrases.Expired, ct); }
        if (result.Value is null) { return new(false, "unavailable", Phrases.NotAvailableNow); }
        session.State = RecoveryState.LinkSent;
        return new(true, "link_sent", Phrases.LinkSent);
    }, ct);
    public Task<ToolResult> CheckResetStatusAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session.State == RecoveryState.Completed) { return CompletedResult(session.UnlockStatus); }
        if (session is not { State: RecoveryState.LinkSent, RecoveryId: { } recoveryId }) { return s_notAllowed; }
        var result = await issuer.GetRecoveryAsync(recoveryId, ct);
        return result.Value switch
        {
            { Status: "completed", ResetReceipt: { } receipt } status => await CompleteAsync(session, receipt, status.UnlockStatus, ct),
            { Status: "reset_failed" } => await EscalateAsync(session, "dependency_unavailable", false, "reset_failed", Phrases.ResetFailed, ct),
            { Status: "expired" } => await EscalateAsync(session, "browser_unavailable", false, "expired", Phrases.LinkExpired, ct),
            _ => new(false, "not_confirmed", Phrases.CantConfirmYet),   // link unused, pending, or issuer unavailable
        };
    }, ct);
    public Task<ToolResult> RequestHumanAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session => session.State switch
    {
        RecoveryState.Completed or RecoveryState.Cancelled => s_notAllowed,
        RecoveryState.Escalated => new(true, "escalated", TicketSentence(session.TicketOutcome == "escalated")),
        _ => await EscalateAsync(session, "human_requested", true, "escalated", Phrases.HumanRequested, ct),
    }, ct);
    public Task<ToolResult> CancelAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (!session.IsOpen) { return s_notAllowed; }
        var linkSent = session.State == RecoveryState.LinkSent;
        session.State = RecoveryState.Cancelled;
        await RecordOutcomeAsync(session, "cancelled", "caller_cancelled", null, ct);
        return new(true, "cancelled", linkSent ? Phrases.CancelledAfterLink : Phrases.Cancelled);
    }, ct);
    /// <summary>Idempotent. The state stays open, so a restart can still reconcile a reset finished after the call (T7).</summary>
    public Task EndCallAsync(string sessionId, string reason, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session.EndedAt is not null) { return false; }
        session.EndedAt = time.GetUtcNow();
        session.EndReason = reason;
        if (session.IsOpen) { await RecordOutcomeAsync(session, "cancelled", "call_dropped", null, ct); }
        return true;
    }, ct);
    private async Task<ToolResult> CompleteAsync(CallSession session, string receipt, string? unlockStatus, CancellationToken ct)
    {
        session.State = RecoveryState.Completed;
        session.ResetReceipt = receipt;
        session.UnlockStatus = unlockStatus;
        await RecordOutcomeAsync(session, "resolved", "reset_completed", receipt, ct);
        return CompletedResult(unlockStatus);
    }
    private static ToolResult CompletedResult(string? unlockStatus) =>
        new(true, "completed", unlockStatus == "unlocked" ? Phrases.CompletedWithUnlock : Phrases.Completed);
    private async Task<ToolResult> EscalateAsync(CallSession session, string reason, bool ok, string status, string firstSentence, CancellationToken ct)
    {
        session.State = RecoveryState.Escalated;
        var recorded = await RecordOutcomeAsync(session, "escalated", reason, null, ct);
        return new(ok, status, $"{firstSentence} {TicketSentence(recorded)}");
    }
    // "A ticket was created" is said only when the issuer accepted it.
    private static string TicketSentence(bool recorded) => recorded ? Phrases.TicketCreated : Phrases.TicketNotCreated;
    // Created right after a recovery starts, so any later failure can be escalated.
    private async Task EnsureTicketAsync(CallSession session, CancellationToken ct)
    {
        if (session.TicketId is not null || session.RecoveryId is null) { return; }
        session.TicketOperationId ??= NewId();
        await sessions.SaveAsync(session, ct);
        var ticket = await issuer.CreateTicketAsync(session.RecoveryId, session.TicketOperationId, ct);
        if (ticket.Value is null) { LogTicketFailed(logger, session.SessionId, "create"); return; }
        session.TicketId = ticket.Value.TicketId;
    }
    // True when the ticket holds this outcome.
    private async Task<bool> RecordOutcomeAsync(CallSession session, string outcome, string reason, string? receipt, CancellationToken ct)
    {
        await EnsureTicketAsync(session, ct);
        if (session.TicketId is null) { return false; }
        if (session.TicketOutcome == outcome && session.TicketReason == reason) { return true; }
        session.OutcomeOperationId = NewId();
        await sessions.SaveAsync(session, ct);
        var result = await issuer.SetTicketOutcomeAsync(session.TicketId, outcome, receipt, reason, session.OutcomeOperationId, ct);
        if (result.Value is null) { LogTicketFailed(logger, session.SessionId, outcome); return false; }
        session.TicketOutcome = outcome;
        session.TicketReason = reason;
        return true;
    }
    // One tool at a time per session; the session is saved after every tool.
    private async Task<T> RunAsync<T>(string sessionId, Func<CallSession, Task<T>> action, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var session = await LoadAsync(sessionId, ct);
            var result = await action(session);
            await sessions.SaveAsync(session, ct);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
    private async Task<CallSession> LoadAsync(string sessionId, CancellationToken ct) =>
        await sessions.GetAsync(sessionId, ct) ?? throw new InvalidOperationException($"Unknown session {sessionId}.");
    private static string NewId() => Guid.NewGuid().ToString("N");
    [LoggerMessage(Level = LogLevel.Warning, Message = "Ticket {Action} failed for session {SessionId}")]
    private static partial void LogTicketFailed(ILogger logger, string sessionId, string action);
}
```

- [ ] **Step 2: `RecoveryServiceCollectionExtensions.AddRecovery(this IServiceCollection services)`**: `AddOptions<IssuerOptions>().BindConfiguration(IssuerOptions.Section).ValidateDataAnnotations().Validate(o => o.BaseUrl.EndsWith('/'), "Issuer:BaseUrl must end with '/'.").ValidateOnStart()`; `AddHttpClient<IssuerClient>((sp, http) => { BaseAddress = new Uri(o.BaseUrl); DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.ServiceCredential); Timeout = TimeSpan.FromSeconds(10); })` + `.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }).SetHandlerLifetime(Timeout.InfiniteTimeSpan)` (the singleton workflow keeps one client); `TryAddSingleton(TimeProvider.System)`, `AddSingleton<SessionStore>()`, `AddSingleton<RecoveryWorkflow>()`.
- [ ] **Step 3: Wire up** — `Program.cs`: `builder.Services.AddRecovery();` after the mock registrations. `appsettings.Development.json`: `"Issuer": { "BaseUrl": "http://localhost:5000/mock/", "ServiceCredential": "<same fake value as Mock:ServiceCredential>" }`.
- [ ] **Step 4: Run the tests** — `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Recovery.RecoveryWorkflowTests"` → `Test run summary: Passed!`, `total: 5`, `failed: 0`.
- [ ] **Step 5: Full check, commit** — `dotnet build VoiceReset.slnx -c Release` → `0 Warning(s)`; `dotnet test --project tests/VoiceReset.Tests` → all pass; `git add solution && git commit -m "feat: add recovery workflow state machine"`

## Self-review

Covered: guard per state; normalization (no issuer call when unclear); IDs saved before each issuer call; ticket right after start; contract outcomes; success only from `completed` + receipt; per-session lock; idempotent `EndCallAsync`; `Unavailable` never success; "ticket created" only when true. 5 required tests. T7 uses `ListOpenAsync`, `IsOpen`, private `RunAsync`/`CompleteAsync`/`RecordOutcomeAsync`.

## Questions

1. T3's dev config must contain user `alex.morgan` (no unlock). OK, or another name?
2. Tests read the newest code/link from `mock/state.json` (`InboxMessage` fields `body`/`link` in camelCase, appended in order; the code is the only 6-digit run in a body). Needs `InMemoryJsonStore` to keep serialized JSON so `ReadAsync<JsonObject>` works. Confirm with T2/T3.
3. `Blue-Harbor-Lantern-42` must pass T4's policy; `POST /mock/v1/resets` must complete at once (200 + receipt); a different `request_id` for an account with an active recovery must get `429 throttled` (T3).
4. `EndCallAsync` records `cancelled/call_dropped` for any open session, also after the `end_call` tool. Use `caller_cancelled` there?
5. A caller-cancelled session with a link already sent is closed, so a reset finished later is not reconciled. Document as a limitation?
6. In Azure, `Issuer:BaseUrl` can be the public host or loopback (`http://localhost:8080/mock/`). Decide in T15.

## Additions to contracts

- `RecoveryWorkflow` also takes `ILogger<RecoveryWorkflow>`. `AddRecovery()` registers options, client, store and workflow.
- `IssuerClient`: `StartRecoveryAsync`, `VerifyAsync`, `SendResetLinkAsync`, `GetRecoveryAsync`, `CreateTicketAsync`, `SetTicketOutcomeAsync`; `IssuerOutcome`, `IssuerResult<T>`, DTOs `RecoveryStarted`, `VerifyResult`, `LinkIssued`, `RecoveryStatus`, `TicketInfo`.
- `CallSession` fields as in Task 1; `SpokenInput.Username/Code`; `Phrases` as listed. `ToolResult.Status`: `not_allowed`, `invalid_username`, `not_started`, `code_sent`, `invalid_code`, `code_incorrect`, `verified`, `exhausted`, `expired`, `link_sent`, `not_confirmed`, `completed`, `reset_failed`, `escalated`, `cancelled`, `unavailable`.
- Test helper `RecoveryAppFactory(InMemoryJsonStore? store = null)`: `Store`, `Time`, `Workflow`, `StartRecoveryAsync(spokenUsername, ct)`, `SessionAsync(id, ct)`, `InboxCodeAsync(ct)`, `InboxLinkAsync(ct)`, `CompleteResetAsync(link, ct)`.
