# T4: Mock Policy, Resets and Tickets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The rest of the mock contract: `GET /mock/v1/policy`, `POST /mock/v1/password/validate`, `POST /mock/v1/resets`
(browser routes, authorized by the reset token) and `POST /mock/v1/tickets`, `POST /mock/v1/tickets/{id}/outcome`
(service routes).

**Architecture:** Adds `MockIssuer.Resets.cs` to the `partial` `MockIssuer` from T3, so the same lock, `RunAsync`,
`WithReplay`, `Hash` and `NewId` apply. `PasswordPolicy` is a static rule table. Passwords are never stored or echoed:
only a SHA-256 of the current password is kept, to enforce "not the current password". The token is found by its
hash; a reset binds the token to its `operation_id`, so a retry of that operation returns the same receipt and any
other operation gets `409 token_used`.

**Tech Stack:** as T3 (needs T3). Commands run from `solution/`. Code blocks omit `using` directives.

**Files:** create `src/VoiceReset/Mock/PasswordPolicy.cs`, `src/VoiceReset/Mock/MockIssuer.Resets.cs`,
`src/VoiceReset/Mock/MockResetEndpoints.cs`, `tests/VoiceReset.Tests/Mock/MockResetEndpointsTests.cs`; modify
`MockState.cs`, `MockContracts.cs`, `MockResult.cs`, `Program.cs`.

### Task 1: Failing tests
- [ ] **Step 1:** `tests/VoiceReset.Tests/Mock/MockResetEndpointsTests.cs` (uses `MockAppFactory` and `Reply` from T3)
```csharp
namespace VoiceReset.Tests.Mock;

public sealed class MockResetEndpointsTests
{
    private const string StrongPassword = "Correct-Horse-42";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reset_SameTokenTwice_OnlyTheFirstOperationSucceeds()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (_, token) = await IssueLinkAsync(app, service);

        var first = await ResetAsync(browser, token, StrongPassword, "reset-1");
        var retry = await ResetAsync(browser, token, StrongPassword, "reset-1");
        var other = await ResetAsync(browser, token, "Another-Horse-43", "reset-2");

        Assert.Equal((HttpStatusCode.OK, "succeeded", "not_required"),
            (first.Status, first.Body.GetProperty("status").GetString(), first.Body.GetProperty("unlock_status").GetString()));
        Assert.Equal(first.Body.GetProperty("reset_receipt").GetString(), retry.Body.GetProperty("reset_receipt").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "token_used"), (other.Status, other.Error));
    }

    [Fact]
    public async Task Reset_WeakPassword_Returns422WithoutEchoingIt()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (_, token) = await IssueLinkAsync(app, service);

        var reply = await ResetAsync(browser, token, "Tiny9", "reset-1");

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "policy_violation"), (reply.Status, reply.Error));
        Assert.Contains(reply.Body.GetProperty("violations").EnumerateArray(), v => v.GetProperty("code").GetString() == "min_length");
        Assert.DoesNotContain("Tiny9", reply.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TicketOutcome_ResolvedWithoutMatchingReceipt_ReturnsInvalidState()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        var (recoveryId, _) = await app.StartAsync(service, "alice", Ct);
        var ticketId = await CreateTicketAsync(service, recoveryId);

        var reply = await SetOutcomeAsync(service, ticketId, "resolved", "rcpt_fabricated", "reset_completed", "o-1");

        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task TicketOutcome_AfterResolved_IsNotOverwritten()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (recoveryId, token) = await IssueLinkAsync(app, service);
        var receipt = (await ResetAsync(browser, token, StrongPassword, "reset-1")).Body.GetProperty("reset_receipt").GetString();
        var ticketId = await CreateTicketAsync(service, recoveryId);

        var resolved = await SetOutcomeAsync(service, ticketId, "resolved", receipt, "reset_completed", "o-1");
        var cancelled = await SetOutcomeAsync(service, ticketId, "cancelled", null, "caller_cancelled", "o-2");

        Assert.Equal((HttpStatusCode.OK, "resolved"), (resolved.Status, resolved.Body.GetProperty("outcome").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (cancelled.Status, cancelled.Error));
    }

    /// <summary>Start, verify and issue a link for alice; read the token from the link's fragment.</summary>
    private static async Task<(string RecoveryId, string Token)> IssueLinkAsync(MockAppFactory app, HttpClient service)
    {
        var (recoveryId, code) = await app.StartAsync(service, "alice", Ct);
        await MockAppFactory.VerifyAsync(service, recoveryId, code, "k1", Ct);
        await MockAppFactory.PostAsync(service, $"/mock/v1/recoveries/{recoveryId}/reset-link", new { operation_id = "link-1" }, Ct);
        var link = (await app.Issuer.GetInboxAsync("alice", Ct))[0].Link ?? "";
        return (recoveryId, Uri.UnescapeDataString(link.Split("#token=")[1]));
    }

    private static Task<Reply> ResetAsync(HttpClient browser, string token, string password, string operationId) =>
        MockAppFactory.PostAsync(browser, "/mock/v1/resets", new { token, new_password = password, operation_id = operationId }, Ct);

    private static async Task<string> CreateTicketAsync(HttpClient service, string recoveryId) =>
        (await MockAppFactory.PostAsync(service, "/mock/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-1" }, Ct))
            .Body.GetProperty("ticket_id").GetString() ?? "";

    private static Task<Reply> SetOutcomeAsync(HttpClient service, string ticketId, string outcome, string? receipt, string reason, string operationId) =>
        MockAppFactory.PostAsync(service, $"/mock/v1/tickets/{ticketId}/outcome",
            new { outcome, reset_receipt = receipt, reason_code = reason, operation_id = operationId }, Ct);
}
```
- [ ] **Step 2:** `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Mock.MockResetEndpointsTests"`
  → 4 tests fail: the routes are not mapped yet, so they return 404 with no body and `SendAsync` throws `JsonException`.

### Task 2: Types and policy
- [ ] **Step 1: `MockContracts.cs`** — append:
```csharp
public sealed record PolicyRule(string Code, string Description);
public sealed record PolicyResponse(string PolicyVersion, IReadOnlyList<PolicyRule> Rules);
public sealed record ValidatePasswordRequest(string Token, string Password);
public sealed record ValidatePasswordResponse(bool Valid, string PolicyVersion, IReadOnlyList<PolicyRule> Violations);
public sealed record ResetRequest(string Token, string NewPassword, string OperationId);
public sealed record ResetResponse(string OperationId, string Status, string? ResetReceipt, string? UnlockStatus, string? ReasonCode);
public sealed record CreateTicketRequest(string RecoveryId, string OperationId);
public sealed record TicketResponse(string TicketId, string RecoveryId, string Outcome);
public sealed record TicketOutcomeRequest(string Outcome, string? ResetReceipt, string ReasonCode, string OperationId);
public sealed record TicketOutcomeResponse(string TicketId, string RecoveryId, string Outcome, string? ResetReceipt, string? ReasonCode);
```
- [ ] **Step 2: `MockState.cs`** — add to `MockState`: `Dictionary<string, string> PasswordHashes { get; set; } = []`
  (normalized username → SHA-256 of the current password; absent = `InitialPassword`) and
  `Dictionary<string, MockTicket> Tickets { get; set; } = []`. Add `record TicketUpdate(DateTimeOffset At, string
  OperationId, string Outcome, string? ReasonCode)` and `sealed class MockTicket`: `const string Open = "open", Resolved =
  "resolved"`; `required string Id`, `required string RecoveryId` (`{ get; init; }`); `string Outcome = Open`,
  `string? ResetReceipt`, `string? ReasonCode`, `List<TicketUpdate> History = []` (`{ get; set; }`).
- [ ] **Step 3: `MockResult.cs`** — add the policy error (no password, only safe rule codes and descriptions):
```csharp
    public static MockResult PolicyViolation(IReadOnlyList<PolicyRule> violations) =>
        Json(422, new PolicyErrorBody(new("policy_violation", "The password does not meet the policy."), violations));
    private sealed record PolicyErrorBody(ErrorDetail Error, IReadOnlyList<PolicyRule> Violations);
```
- [ ] **Step 4: `src/VoiceReset/Mock/PasswordPolicy.cs`**
```csharp
namespace VoiceReset.Mock;

/// <summary>The synthetic password policy. Descriptions are safe to display or speak.</summary>
public static class PasswordPolicy
{
    public const string Version = "2026-10-v1";

    private static readonly (PolicyRule Rule, Func<string, string, bool, bool> Passes)[] s_rules =
    [
        (new("min_length", "Use at least 12 characters."), (password, _, _) => password.Length >= 12),
        (new("uppercase", "Include an uppercase letter."), (password, _, _) => password.Any(char.IsUpper)),
        (new("lowercase", "Include a lowercase letter."), (password, _, _) => password.Any(char.IsLower)),
        (new("digit", "Include a digit."), (password, _, _) => password.Any(char.IsDigit)),
        (new("not_username", "Do not include your username."),
            (password, username, _) => !password.Contains(username, StringComparison.OrdinalIgnoreCase)),
        (new("not_current", "Do not reuse your current password."), (_, _, isCurrent) => !isCurrent),
    ];

    public static IReadOnlyList<PolicyRule> Rules { get; } = [.. s_rules.Select(r => r.Rule)];

    /// <summary>The rules the password breaks; empty means valid.</summary>
    public static IReadOnlyList<PolicyRule> Check(string password, string username, bool isCurrentPassword) =>
        [.. s_rules.Where(r => !r.Passes(password, username, isCurrentPassword)).Select(r => r.Rule)];
}
```

### Task 3: Rules, endpoints, wiring
- [ ] **Step 1: `src/VoiceReset/Mock/MockIssuer.Resets.cs`**
```csharp
namespace VoiceReset.Mock;

public sealed partial class MockIssuer
{
    private static readonly string[] s_safeReasons = ["browser_unavailable", "verification_exhausted", "verification_expired",
        "human_requested", "caller_cancelled", "call_dropped", "dependency_unavailable", "completion_unknown"];

    public Task<MockResult> ValidatePasswordAsync(ValidatePasswordRequest request, CancellationToken ct) => RunAsync(now =>
        FindByToken(request.Token) is not { } recovery || FindUser(recovery.Username) is not { } user ? InvalidToken()
        : TokenRefusal(recovery, now) ?? MockResult.Json(200, Validate(user, request.Password)), save: false, ct);

    public Task<MockResult> ResetPasswordAsync(ResetRequest request, CancellationToken ct) =>
        RunAsync(now => ResetPassword(request, now), save: true, ct);

    public Task<MockResult> CreateTicketAsync(CreateTicketRequest request, CancellationToken ct) => RunAsync(_ =>
        !_state.Recoveries.ContainsKey(request.RecoveryId) ? MockResult.NotFound()
        : WithReplay($"ticket:{request.OperationId}", Hash(request.RecoveryId), () => CreateTicket(request.RecoveryId)), save: true, ct);

    public Task<MockResult> SetTicketOutcomeAsync(string ticketId, TicketOutcomeRequest request, CancellationToken ct) => RunAsync(now =>
        !_state.Tickets.TryGetValue(ticketId, out var ticket) ? MockResult.NotFound()
        : WithReplay($"outcome:{ticketId}:{request.OperationId}", Hash($"{request.Outcome}|{request.ResetReceipt}|{request.ReasonCode}"),
            () => ApplyOutcome(ticket, request, now)), save: true, ct);

    private MockResult ResetPassword(ResetRequest request, DateTimeOffset now)
    {
        if (FindByToken(request.Token) is not { } recovery || FindUser(recovery.Username) is not { } user)
        {
            return InvalidToken();
        }
        if (recovery.ResetOperationId == request.OperationId) // a retry of the accepted operation: same result, even after use/expiry
        {
            return ResetSucceeded(request.OperationId, recovery);
        }
        if (TokenRefusal(recovery, now) is { } refusal)
        {
            return refusal;
        }
        var validation = Validate(user, request.NewPassword);
        if (!validation.Valid)
        {
            return MockResult.PolicyViolation(validation.Violations); // no reset, the token stays usable
        }
        // Password and any required unlock commit together; only this produces a receipt.
        _state.PasswordHashes[recovery.Username] = Hash(request.NewPassword);
        recovery.ResetOperationId = request.OperationId;
        recovery.ResetReceipt = NewId("rcpt_");
        recovery.UnlockStatus = user.RequiresUnlock ? "unlocked" : "not_required";
        recovery.Status = RecoveryStatus.Completed;
        return ResetSucceeded(request.OperationId, recovery);
    }

    private MockResult CreateTicket(string recoveryId)
    {
        var ticketId = $"tkt_{recoveryId}"; // one ticket per recovery
        if (_state.Tickets.TryGetValue(ticketId, out var existing))
        {
            return MockResult.Json(200, new TicketResponse(existing.Id, existing.RecoveryId, existing.Outcome));
        }
        _state.Tickets[ticketId] = new MockTicket { Id = ticketId, RecoveryId = recoveryId };
        return MockResult.Json(201, new TicketResponse(ticketId, recoveryId, MockTicket.Open));
    }

    private MockResult ApplyOutcome(MockTicket ticket, TicketOutcomeRequest request, DateTimeOffset now)
    {
        var resolving = request.Outcome == MockTicket.Resolved;
        if (!resolving && !IsSafeNonResolution(request))
        {
            return MockResult.InvalidRequest();
        }
        // Resolved needs this recovery's issuer receipt; a confirmed success is never overwritten.
        var hasReceipt = request.ReasonCode == "reset_completed" && request.ResetReceipt is { } receipt
            && _state.Recoveries.TryGetValue(ticket.RecoveryId, out var recovery) && recovery.ResetReceipt == receipt;
        if (resolving ? !hasReceipt : ticket.Outcome == MockTicket.Resolved)
        {
            return MockResult.InvalidState();
        }
        (ticket.Outcome, ticket.ResetReceipt, ticket.ReasonCode) = (request.Outcome, request.ResetReceipt, request.ReasonCode);
        ticket.History.Add(new TicketUpdate(now, request.OperationId, request.Outcome, request.ReasonCode));
        return MockResult.Json(200, new TicketOutcomeResponse(ticket.Id, ticket.RecoveryId, ticket.Outcome, ticket.ResetReceipt, ticket.ReasonCode));
    }

    private static bool IsSafeNonResolution(TicketOutcomeRequest r) =>
        r.Outcome is "escalated" or "cancelled" or "pending" && r.ResetReceipt is null && s_safeReasons.Contains(r.ReasonCode);

    private ValidatePasswordResponse Validate(MockUser user, string password)
    {
        var current = _state.PasswordHashes.GetValueOrDefault(Normalize(user.Username)) ?? Hash(user.InitialPassword);
        var violations = PasswordPolicy.Check(password, user.Username, isCurrentPassword: Hash(password) == current);
        return new ValidatePasswordResponse(violations.Count == 0, PasswordPolicy.Version, violations);
    }

    private MockRecovery? FindByToken(string token) => _state.Recoveries.Values.FirstOrDefault(r => r.TokenHash == Hash(token));

    private static MockResult? TokenRefusal(MockRecovery recovery, DateTimeOffset now) =>
        recovery.ResetOperationId is not null ? MockResult.Fail(409, "token_used", "This link has already been used.")
        : now >= recovery.LinkExpiresAt ? MockResult.Fail(410, "link_expired", "This link has expired.")
        : null;

    private static MockResult InvalidToken() => MockResult.Fail(401, "invalid_token", "This link is not valid.");

    private static MockResult ResetSucceeded(string operationId, MockRecovery r) =>
        MockResult.Json(200, new ResetResponse(operationId, "succeeded", r.ResetReceipt, r.UnlockStatus, ReasonCode: null));
}
```
- [ ] **Step 2: `src/VoiceReset/Mock/MockResetEndpoints.cs`**
```csharp
namespace VoiceReset.Mock;

public static class MockResetEndpoints
{
    public static IEndpointRouteBuilder MapMockReset(this IEndpointRouteBuilder endpoints)
    {
        // Browser routes: no service credential; the reset token in the body is the only authority.
        var browser = endpoints.MapGroup("/mock/v1");
        browser.MapGet("/policy", () => MockResult.Json(200, new PolicyResponse(PasswordPolicy.Version, PasswordPolicy.Rules)));
        browser.MapPost("/password/validate", async (HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
            await MockJson.ReadAsync<ValidatePasswordRequest>(request, ct) is { } body ? await issuer.ValidatePasswordAsync(body, ct) : MockResult.InvalidRequest());
        browser.MapPost("/resets", async (HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
            await MockJson.ReadAsync<ResetRequest>(request, ct) is { } body ? await issuer.ResetPasswordAsync(body, ct) : MockResult.InvalidRequest());

        var tickets = endpoints.MapGroup("/mock/v1/tickets").AddEndpointFilter(MockIssuerEndpoints.RequireServiceCredentialAsync);
        tickets.MapPost("", async (HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
            await MockJson.ReadAsync<CreateTicketRequest>(request, ct) is { } body ? await issuer.CreateTicketAsync(body, ct) : MockResult.InvalidRequest());
        tickets.MapPost("/{id}/outcome", async (string id, HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
            await MockJson.ReadAsync<TicketOutcomeRequest>(request, ct) is { } body ? await issuer.SetTicketOutcomeAsync(id, body, ct) : MockResult.InvalidRequest());
        return endpoints;
    }
}
```
- [ ] **Step 3:** In `src/VoiceReset/Program.cs` add `app.MapMockReset();` after `app.MapMockIssuer();`.
- [ ] **Step 4:** Rerun the filtered tests → `Test run summary: Passed!`, total 4, failed 0. Then
  `dotnet build VoiceReset.slnx -c Release` → 0 warnings, and `dotnet test --project tests/VoiceReset.Tests` → total 17, failed 0.
- [ ] **Step 5:** `git add src/VoiceReset tests/VoiceReset.Tests` and `git commit -m "feat(mock): add policy, reset and ticket endpoints"`

## Self-review

- Policy: version `2026-10-v1`, rules min 12 / upper / lower / digit / not username / not current ✓. Validate: same
  token checks as reset, does not consume ✓. Reset: token valid (401), unused (409 `token_used`), unexpired (410
  `link_expired`); same `operation_id` → same result (test 1); success → receipt, `unlocked`/`not_required`, recovery
  `completed`; policy failure → 422 with safe violations, no echo (test 2) ✓. Tickets: one per recovery (second create →
  200 same, same-key retry → recorded 201), `resolved` only with the recovery's receipt + `reset_completed` (test 3),
  resolved never overwritten (test 4), idempotent per `operation_id`, history kept ✓.
- Task 1 compiles against T3's test helpers, so "red" is the unmapped-route failure, not a build error.

## Questions

1. A human-requested escalation can still be replaced by `resolved` when a matching receipt arrives (the contract asks
   to "preserve" it). Should `escalated`/`human_requested` also block `resolved`, or is history enough?
2. Validation and reset accept a token from an unknown user only if the user was removed from settings after the link
   was issued (→ 401). Fine?
3. `FindByToken` scans all recoveries (fine for a handful). Worth a token-hash index?
4. `GET /v1/reset-operations/{id}` and pending/failed resets are skipped (the plan's scope). Confirm.

## Additions to contracts

- Records: `PolicyRule`, `PolicyResponse`, `ValidatePasswordRequest/Response`, `ResetRequest`, `ResetResponse`,
  `CreateTicketRequest`, `TicketResponse`, `TicketOutcomeRequest`, `TicketOutcomeResponse`.
- `MockState.PasswordHashes`, `MockState.Tickets`; `MockTicket` (`Open`, `Resolved`), `TicketUpdate`;
  `MockResult.PolicyViolation`; `PasswordPolicy` (`Version`, `Rules`, `Check`).
- `MockIssuer`: `ValidatePasswordAsync`, `ResetPasswordAsync`, `CreateTicketAsync`, `SetTicketOutcomeAsync`;
  replay scopes `ticket:{operationId}` and `outcome:{ticketId}:{operationId}`.
- Unlock status values: `unlocked`, `not_required`. Reset status here is always `succeeded` (no pending/failed in the mock).
