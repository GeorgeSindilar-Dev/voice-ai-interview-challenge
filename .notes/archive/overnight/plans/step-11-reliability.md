# Step 11: Reliability Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove, with tests and recorded evidence, the six behaviours the assessors exercise: ambiguous/invalid speech, timeouts, cancellation/dropped calls, duplicate events, concurrent sessions, and process restarts. Fix any gap these tests reveal.

**Architecture:** Most reliability **mechanisms** already exist: idempotency keys and reconciliation (step 6), contract rules in the mocks (step 5), limits, timers and the tool dispatcher (step 7). This step adds the **cross-component tests** that show they work together. Step 6's `FakeIssuerHandler` is *scripted* (one answer per request), but restart and multi-call tests need an issuer that **remembers state**. Task 0 therefore adds a small `StatefulIssuerHandler` and an `AgentTestHost` (test code only). There's no new production architecture. Production changes happen only where a test reveals a gap.

**Tech Stack:** xUnit v3, `FakeTimeProvider`, step 6 types (`RecoveryWorkflow`, `InMemorySessionStore`, `ReconciliationService`, `IssuerClient`, `IssuerSamples`), the Azure CLI.

**When:** Sunday. About 3 hours.

**Depends on:** steps 5, 6, 7 implemented. Names are taken from the step 6 plan's "Public surface" and its test helpers (`IssuerSamples`, `WorkflowHarness`). For step 7 names (`ToolDispatcher`), check the final step 7 code and adjust only names, never assertions.

---

## File structure

| File | Responsibility |
|---|---|
| `tests/VoiceReset.Agent.Tests/Fakes/StatefulIssuerHandler.cs` | In-memory issuer with state (recoveries, attempts, links, tickets, receipts, outages) |
| `tests/VoiceReset.Agent.Tests/Fakes/AgentTestHost.cs` | Real `RecoveryWorkflow` + `ReconciliationService` over a shared store and issuer |
| `tests/VoiceReset.Agent.Tests/Reliability/RestartTests.cs` | Restart mid-flow → truthful reconciliation |
| `tests/VoiceReset.Agent.Tests/Reliability/DuplicateEventTests.cs` | The same tool call delivered twice → one effect |
| `tests/VoiceReset.Agent.Tests/Reliability/ConcurrencyTests.cs` | Isolation, same account twice, ETag conflicts |
| `tests/VoiceReset.Agent.Tests/Reliability/OutageTests.cs` | Network failure / issuer down → honest unknown, then recovery |
| `tests/VoiceReset.Agent.Tests/Reliability/DroppedCallTests.cs` | Drop in every state; later receipt → resolved; human request kept |
| `tests/VoiceReset.Agent.Tests/Reliability/AmbiguousSpeechTests.cs` | Fragments and words never burn attempts |
| `tests/VoiceReset.Mocks.Tests/Reliability/IssuerDurabilityTests.cs` | Mocks: counters and expiry survive a mocks restart |
| `docs/process/reliability-evidence.md`, `docs/process/restart-test-evidence.md` | Evidence |

---

### Task 0: Stateful fake issuer and test host

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Fakes/StatefulIssuerHandler.cs`
- Create: `tests/VoiceReset.Agent.Tests/Fakes/AgentTestHost.cs`

- [ ] **Step 1: Write `StatefulIssuerHandler`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiceReset.Agent.Tests.Fakes;

/// <summary>
/// A small in-memory issuer that remembers state between calls (unlike the scripted
/// FakeIssuerHandler). It implements only the rules the agent depends on: one active
/// recovery per account, two attempts, idempotent verify, links, tickets and receipts,
/// plus switches for outages and network failures. Responses reuse IssuerSamples so the
/// JSON shapes match the contract exactly.
/// </summary>
public sealed partial class StatefulIssuerHandler : HttpMessageHandler
{
    public const string DefaultCode = "047192";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FakeRecovery> _recoveries = [];
    private readonly Dictionary<string, string> _recoveryByRequestId = [];
    private readonly Dictionary<string, (int Status, string Json)> _verifyByKey = [];
    private readonly Dictionary<string, FakeTicket> _tickets = [];
    private readonly List<(string Method, string Path)> _requests = [];
    private readonly Queue<Regex> _networkFailures = new();
    private int _nextId;

    /// <summary>While true, every request answers 503 dependency_unavailable.</summary>
    public bool Down { get; set; }

    public void FailNextWithNetworkError(string pathPattern)
    {
        lock (_gate)
        {
            _networkFailures.Enqueue(new Regex(pathPattern));
        }
    }

    public int Count(string method, string pathPattern)
    {
        lock (_gate)
        {
            return _requests.Count(r => r.Method == method && Regex.IsMatch(r.Path, pathPattern));
        }
    }

    public string CodeFor(string username)
    {
        lock (_gate)
        {
            return _recoveries.Values.Last(r => r.Username == username).Code;
        }
    }

    /// <summary>Simulates the caller finishing the browser form.</summary>
    public void CompleteResetFor(string username, string receipt, string unlockStatus)
    {
        lock (_gate)
        {
            FakeRecovery recovery = _recoveries.Values.Last(r => r.Username == username);
            recovery.Status = "completed";
            recovery.Receipt = receipt;
            recovery.UnlockStatus = unlockStatus;
        }
    }

    public FakeTicket TicketFor(string recoveryId)
    {
        lock (_gate)
        {
            return _tickets[$"tkt-{recoveryId}"];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        string path = request.RequestUri!.AbsolutePath;
        string method = request.Method.Method;
        string? idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? keys) ? keys.Single() : null;

        lock (_gate)
        {
            _requests.Add((method, path));
            if (_networkFailures.Count > 0 && _networkFailures.Peek().IsMatch(path))
            {
                _networkFailures.Dequeue();
                throw new HttpRequestException("Simulated network failure.");
            }
            if (Down)
            {
                return Json(503, IssuerSamples.Error("dependency_unavailable"));
            }
            return Route(method, path, body, idempotencyKey);
        }
    }

    private HttpResponseMessage Route(string method, string path, string body, string? idempotencyKey)
    {
        JsonElement json = body.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(body);
        Match match;
        if (method == "POST" && path == "/v1/recoveries")
        {
            return StartRecovery(json.GetProperty("username").GetString()!, json.GetProperty("request_id").GetString()!);
        }
        if (method == "GET" && path == "/v1/policy")
        {
            return Json(200, IssuerSamples.Policy);
        }
        if (method == "POST" && path == "/v1/tickets")
        {
            return CreateTicket(json.GetProperty("recovery_id").GetString()!);
        }
        if (method == "POST" && (match = VerifyPath().Match(path)).Success)
        {
            return Verify(match.Groups[1].Value, json.GetProperty("code").GetString()!, idempotencyKey!);
        }
        if (method == "POST" && (match = LinkPath().Match(path)).Success)
        {
            return SendLink(match.Groups[1].Value);
        }
        if (method == "GET" && (match = RecoveryPath().Match(path)).Success)
        {
            return GetRecovery(match.Groups[1].Value);
        }
        if (method == "POST" && (match = OutcomePath().Match(path)).Success)
        {
            return UpdateOutcome(match.Groups[1].Value, json);
        }
        throw new InvalidOperationException($"StatefulIssuerHandler has no route for {method} {path}.");
    }

    private HttpResponseMessage StartRecovery(string username, string requestId)
    {
        if (_recoveryByRequestId.TryGetValue(requestId, out string? existing))
        {
            return Json(202, IssuerSamples.RecoveryAccepted(existing));
        }
        if (_recoveries.Values.Any(r => r.Username == username && r.IsActive))
        {
            return Json(429, IssuerSamples.Error("throttled"), retryAfterSeconds: 120);
        }
        string id = $"rec-{++_nextId}";
        _recoveries[id] = new FakeRecovery(id, username, DefaultCode);
        _recoveryByRequestId[requestId] = id;
        return Json(202, IssuerSamples.RecoveryAccepted(id));
    }

    private HttpResponseMessage Verify(string recoveryId, string code, string key)
    {
        if (_verifyByKey.TryGetValue(key, out (int Status, string Json) recorded))
        {
            return Json(recorded.Status, recorded.Json);   // idempotent replay: no second attempt
        }
        FakeRecovery recovery = _recoveries[recoveryId];
        (int Status, string Json) result;
        if (recovery.Status is "verified" or "link_issued" or "completed")
        {
            result = (409, IssuerSamples.Error("invalid_state"));
        }
        else if (recovery.Status == "exhausted")
        {
            result = (409, IssuerSamples.VerificationExhausted());
        }
        else if (code == recovery.Code)
        {
            recovery.Status = "verified";
            result = (200, IssuerSamples.Verified(recoveryId));
        }
        else
        {
            recovery.Attempts--;
            if (recovery.Attempts == 0)
            {
                recovery.Status = "exhausted";
                result = (409, IssuerSamples.VerificationExhausted());
            }
            else
            {
                result = (422, IssuerSamples.VerificationFailed());
            }
        }
        _verifyByKey[key] = result;
        return Json(result.Status, result.Json);
    }

    private HttpResponseMessage SendLink(string recoveryId)
    {
        FakeRecovery recovery = _recoveries[recoveryId];
        if (recovery.Status is not ("verified" or "link_issued"))
        {
            return Json(409, IssuerSamples.Error("invalid_state"));
        }
        recovery.Status = "link_issued";
        return Json(200, IssuerSamples.LinkIssued(recoveryId));
    }

    private HttpResponseMessage GetRecovery(string recoveryId)
    {
        FakeRecovery recovery = _recoveries[recoveryId];
        return Json(200, IssuerSamples.Recovery(recoveryId, recovery.Status, recovery.Receipt, recovery.UnlockStatus));
    }

    private HttpResponseMessage CreateTicket(string recoveryId)
    {
        string id = $"tkt-{recoveryId}";
        if (_tickets.ContainsKey(id))
        {
            return Json(200, IssuerSamples.TicketCreated(id, recoveryId));
        }
        _tickets[id] = new FakeTicket(id, recoveryId);
        return Json(201, IssuerSamples.TicketCreated(id, recoveryId));
    }

    private HttpResponseMessage UpdateOutcome(string ticketId, JsonElement json)
    {
        FakeTicket ticket = _tickets[ticketId];
        string outcome = json.GetProperty("outcome").GetString()!;
        string? receipt = json.GetProperty("reset_receipt").GetString();
        string reason = json.GetProperty("reason_code").GetString()!;
        if (outcome == "resolved" && receipt != _recoveries[ticket.RecoveryId].Receipt)
        {
            return Json(409, IssuerSamples.Error("invalid_state"));   // fabricated or cross-recovery receipt
        }
        if (ticket.Outcome != "resolved")                             // never overwrite a confirmed success
        {
            ticket.Outcome = outcome;
            ticket.Reason = reason;
            ticket.Receipt = receipt;
        }
        ticket.History.Add($"{outcome}/{reason}");
        string response = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["ticket_id"] = ticket.Id,
            ["recovery_id"] = ticket.RecoveryId,
            ["outcome"] = ticket.Outcome,
            ["reset_receipt"] = ticket.Receipt,
            ["reason_code"] = ticket.Reason,
        });
        return Json(200, response);
    }

    private static HttpResponseMessage Json(int status, string json, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is int seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }
        return response;
    }

    [GeneratedRegex("^/v1/recoveries/([^/]+)/verify$")]
    private static partial Regex VerifyPath();

    [GeneratedRegex("^/v1/recoveries/([^/]+)/reset-link$")]
    private static partial Regex LinkPath();

    [GeneratedRegex("^/v1/recoveries/([^/]+)$")]
    private static partial Regex RecoveryPath();

    [GeneratedRegex("^/v1/tickets/([^/]+)/outcome$")]
    private static partial Regex OutcomePath();
}

public sealed class FakeRecovery(string id, string username, string code)
{
    public string Id { get; } = id;
    public string Username { get; } = username;
    public string Code { get; } = code;
    public string Status { get; set; } = "awaiting_verification";
    public int Attempts { get; set; } = 2;
    public string? Receipt { get; set; }
    public string? UnlockStatus { get; set; }
    public bool IsActive => Status is "awaiting_verification" or "verified" or "link_issued" or "reset_pending";
}

public sealed class FakeTicket(string id, string recoveryId)
{
    public string Id { get; } = id;
    public string RecoveryId { get; } = recoveryId;
    public string Outcome { get; set; } = "open";
    public string? Reason { get; set; }
    public string? Receipt { get; set; }
    public List<string> History { get; } = [];
}
```

- [ ] **Step 2: Write `AgentTestHost`** (built the same way as step 6's `WorkflowHarness`, but the store and issuer are passed in, so two hosts can share them, simulating a restart):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VoiceReset.Agent.Configuration;
using VoiceReset.Agent.Issuer;
using VoiceReset.Agent.Recovery;

namespace VoiceReset.Agent.Tests.Fakes;

/// <summary>One "process": a real workflow and reconciler over a store and issuer that can outlive it.</summary>
public sealed class AgentTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly HttpClient _http;

    public AgentTestHost(InMemorySessionStore store, HttpMessageHandler issuer, TimeProvider time, LimitsOptions? limits = null)
    {
        Store = store;
        _http = new HttpClient(issuer, disposeHandler: false) { BaseAddress = new Uri("https://issuer.test/") };
        var client = new IssuerClient(_http, NullLogger<IssuerClient>.Instance);
        Workflow = new RecoveryWorkflow(store, client, new SessionLocks(), time, Options.Create(limits ?? new LimitsOptions()),
            NullLogger<RecoveryWorkflow>.Instance);
        _provider = new ServiceCollection().AddSingleton(Workflow).BuildServiceProvider();
        Reconciler = new ReconciliationService(_provider.GetRequiredService<IServiceScopeFactory>(), store,
            Options.Create(new ReconciliationOptions()), time, NullLogger<ReconciliationService>.Instance);
    }

    public InMemorySessionStore Store { get; }
    public RecoveryWorkflow Workflow { get; }
    public ReconciliationService Reconciler { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Walks a new session to the target state through the real tools.</summary>
    public async Task<string> DriveToAsync(RecoveryState target, StatefulIssuerHandler issuer, string username = "alex.morgan")
    {
        string id = await Workflow.StartSessionAsync(CallChannel.Browser, Ct);
        if (target == RecoveryState.AwaitingUsername)
        {
            return id;
        }
        await Workflow.StartRecoveryAsync(id, username, Ct);
        if (target == RecoveryState.AwaitingCode)
        {
            return id;
        }
        await Workflow.SubmitCodeAsync(id, issuer.CodeFor(username), Ct);
        if (target == RecoveryState.Verified)
        {
            return id;
        }
        await Workflow.SendResetLinkAsync(id, Ct);
        return id;
    }

    public async Task<CallSession> SessionAsync(string id) =>
        await Store.GetAsync(id, Ct) ?? throw new InvalidOperationException("The session should exist.");

    public async ValueTask DisposeAsync()
    {
        Reconciler.Dispose();
        _http.Dispose();
        await _provider.DisposeAsync();
    }
}
```

If step 6's `RecoveryWorkflow` or `ReconciliationService` constructors end up different from the plan (check `WorkflowHarness` in the real code), mirror the real ones here.
- [ ] **Step 3: Build**

```powershell
dotnet build tests/VoiceReset.Agent.Tests -c Release
```

Expected: `Build succeeded.` with 0 warnings. Fix any analyzer warnings in the new files (Release treats warnings as errors).
- [ ] **Step 4: Commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Fakes/StatefulIssuerHandler.cs solution/tests/VoiceReset.Agent.Tests/Fakes/AgentTestHost.cs
git commit -m "test: add stateful fake issuer and agent test host"
```

---

### Task 1: Restart mid-flow

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Reliability/RestartTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Agent.Configuration;
using VoiceReset.Agent.Issuer;
using VoiceReset.Agent.Recovery;
using VoiceReset.Agent.Tests.Fakes;

namespace VoiceReset.Agent.Tests.Reliability;

public sealed class RestartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunOnce_AfterRestartInLinkSent_ResolvesTicketWithReceipt()
    {
        // Arrange: state that survives the "restart"
        var store = new InMemorySessionStore();
        var issuer = new StatefulIssuerHandler();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero));
        string sessionId;
        await using (var before = new AgentTestHost(store, issuer, time))
        {
            sessionId = await before.DriveToAsync(RecoveryState.LinkSent, issuer);
        } // the process "dies": EndAsync is never called

        issuer.CompleteResetFor("alex.morgan", receipt: "rcpt-1", unlockStatus: "not_required");
        time.Advance(TimeSpan.FromMinutes(2));

        // Act: a new process starts and runs its first reconciliation pass
        await using var after = new AgentTestHost(store, issuer, time);
        await after.Reconciler.RunOnceAsync(Ct);

        // Assert
        CallSession session = await after.SessionAsync(sessionId);
        Assert.Equal(TicketOutcomes.Resolved, session.TicketOutcome);
        Assert.Equal(TicketReasons.ResetCompleted, session.TicketReason);
        Assert.Equal("rcpt-1", issuer.TicketFor(session.RecoveryId!).Receipt);
    }

    [Fact]
    public async Task RunOnce_AfterRestartInAwaitingCodeLongAgo_EndsStaleSession()
    {
        var store = new InMemorySessionStore();
        var issuer = new StatefulIssuerHandler();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero));
        string sessionId;
        await using (var before = new AgentTestHost(store, issuer, time))
        {
            sessionId = await before.DriveToAsync(RecoveryState.AwaitingCode, issuer);
        }
        time.Advance(TimeSpan.FromSeconds(new LimitsOptions().MaxCallSecondsBrowser + 61));

        await using var after = new AgentTestHost(store, issuer, time);
        await after.Reconciler.RunOnceAsync(Ct);

        CallSession session = await after.SessionAsync(sessionId);
        Assert.Equal(CallEndReason.StaleAfterRestart, session.EndReason);
        Assert.Equal(TicketReasons.CallDropped, session.TicketReason);
    }

    [Fact]
    public async Task RunOnce_IssuerDownThenBack_PendingThenResolved()
    {
        var store = new InMemorySessionStore();
        var issuer = new StatefulIssuerHandler();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero));
        await using var host = new AgentTestHost(store, issuer, time);
        string sessionId = await host.DriveToAsync(RecoveryState.LinkSent, issuer);
        await host.Workflow.EndAsync(sessionId, CallEndReason.ConnectionLost, Ct);
        issuer.CompleteResetFor("alex.morgan", receipt: "rcpt-2", unlockStatus: "not_required");

        issuer.Down = true;
        await host.Reconciler.RunOnceAsync(Ct);
        Assert.NotEqual(TicketOutcomes.Resolved, (await host.SessionAsync(sessionId)).TicketOutcome);

        issuer.Down = false;
        await host.Reconciler.RunOnceAsync(Ct);
        Assert.Equal(TicketOutcomes.Resolved, (await host.SessionAsync(sessionId)).TicketOutcome);
    }
}
```

- [ ] **Step 2: Run**

```powershell
dotnet test --project tests/VoiceReset.Agent.Tests --filter-class "VoiceReset.Agent.Tests.Reliability.RestartTests"
```

Expected: 3 passed. A failure is a real gap: fix the reconciler or workflow (not the test), re-run, and note the fix for Task 9.
- [ ] **Step 3: Commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Reliability/RestartTests.cs
git commit -m "test(reliability): restart mid-flow reconciles tickets truthfully"
```

---

### Task 2: Duplicate tool call events

Voice Live identifies each function call by a `call_id`. If one call is delivered
twice (reconnect, retry, our own bug), the second delivery must not reach the
issuer again. Two **distinct** caller submissions of the same wrong code are
different calls and **do** count twice, as the contract says. Step 7's
`ToolDispatcher` never sees the `call_id`. `VoiceSession.OnFunctionCallAsync`
does, so de-duplication belongs there.

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Reliability/DuplicateEventTests.cs`
- Modify (only if the first test fails): `src/VoiceReset.Agent/Voice/VoiceSession.cs` (`OnFunctionCallAsync`)

- [ ] **Step 1: Write the tests.** The first uses step 7's `VoiceSessionHarness` (real `VoiceSession` + `WorkflowHarness` with the scripted step 6 issuer). The second uses step 7's `ToolDispatcher` directly on an `AgentTestHost`:

```csharp
using Azure.AI.VoiceLive;
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Agent.Recovery;
using VoiceReset.Agent.Tests.Fakes;
using VoiceReset.Agent.Tests.Issuer;
using VoiceReset.Agent.Tests.Voice;
using VoiceReset.Agent.Voice;

namespace VoiceReset.Agent.Tests.Reliability;

public sealed class DuplicateEventTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FunctionCall_SameCallIdDeliveredTwice_CallsIssuerOnce()
    {
        using var h = new VoiceSessionHarness();
        await h.StartAsync();
        await h.AgentFinishesAnswerAsync();
        h.Workflow.ScriptSuccessfulStart();
        await h.ModelCallsToolAsync(ToolNames.StartRecovery, """{"username":"alex.morgan"}""", callId: "call-1");
        h.VoiceLive.Emit(VoiceSessionHarness.ResponseDone(SessionResponseStatus.Completed));
        h.Workflow.Issuer.Respond("POST", $"/v1/recoveries/{WorkflowHarness.RecoveryId}/verify", 422, IssuerSamples.VerificationFailed());

        // The same function call event arrives twice
        await h.ModelCallsToolAsync(ToolNames.SubmitCode, """{"code":"111111"}""", callId: "call-2");
        h.VoiceLive.Emit(VoiceLiveModelFactory.SessionUpdateResponseFunctionCallArgumentsDone(
            callId: "call-2", arguments: """{"code":"111111"}""", name: ToolNames.SubmitCode));
        await h.VoiceLive.SyncAsync();

        Assert.Single(h.Workflow.Issuer.RequestsTo("POST", $"/v1/recoveries/{WorkflowHarness.RecoveryId}/verify"));
        var outputs = h.VoiceLive.Activity.Items.OfType<FunctionOutputSent>().Where(o => o.CallId == "call-2").ToList();
        Assert.Equal(2, outputs.Count);                          // the repeat still gets an answer...
        Assert.Equal(outputs[0].OutputJson, outputs[1].OutputJson);  // ...the recorded one
    }

    [Fact]
    public async Task Dispatch_SameWrongCodeAsTwoSeparateSubmissions_CountsTwoAttempts()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string sessionId = await host.DriveToAsync(RecoveryState.AwaitingCode, issuer);
        var dispatcher = new ToolDispatcher(host.Workflow);

        await dispatcher.DispatchAsync(sessionId, ToolNames.SubmitCode, """{"code":"111111"}""", Ct);
        ToolResult second = await dispatcher.DispatchAsync(sessionId, ToolNames.SubmitCode, """{"code":"111111"}""", Ct);

        Assert.Equal(ToolStatus.VerificationExhausted, second.Status);
        Assert.Equal(2, issuer.Count("POST", "/verify$"));
    }
}
```

The `using` lines for the test helpers must match the namespaces where steps 6 and 7
put `IssuerSamples`, `WorkflowHarness`, `VoiceSessionHarness` and the activity
records (`FunctionOutputSent`). Fix the namespaces if the build complains.
`ToolDispatcher`'s constructor may need more arguments than shown (check step 7's
`ToolDispatcherTests`); copy them from there.
- [ ] **Step 2: Run.** If `FunctionCall_SameCallIdDeliveredTwice_CallsIssuerOnce` fails, add the check at the top of `VoiceSession.OnFunctionCallAsync`, using a per-session dictionary. It's in memory: a restart closes the Voice Live session anyway, and step 6's idempotency keys protect the issuer across restarts.

```csharp
// VoiceSession field
private readonly Dictionary<string, string> _handledCalls = new(StringComparer.Ordinal);

// first lines of OnFunctionCallAsync(SessionUpdateResponseFunctionCallArgumentsDone call, CancellationToken ct)
if (_handledCalls.TryGetValue(call.CallId, out string? recordedOutput))
{
    await _voiceLive.SendFunctionOutputAsync(call.CallId, recordedOutput, ct);
    return;
}
// ... existing body; right after the output JSON is built:
_handledCalls[call.CallId] = outputJson;
```

Use the real field and variable names from step 7's `VoiceSession` (the Voice Live
connection field and the output JSON variable). After the repeat, follow the same
"ask for the next response after response.done" logic as for a first delivery.
- [ ] **Step 3: Run until both pass, then commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Reliability/DuplicateEventTests.cs solution/src/VoiceReset.Agent/Voice/VoiceSession.cs
git commit -m "fix(voice): answer repeated function call events without calling the issuer again"
```

---

### Task 3: Concurrent sessions

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Reliability/ConcurrencyTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using VoiceReset.Agent.Recovery;
using VoiceReset.Agent.Tests.Fakes;

namespace VoiceReset.Agent.Tests.Reliability;

public sealed class ConcurrencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartRecovery_TwentyParallelSessions_EachGetsItsOwnRecovery()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, TimeProvider.System);
        string[] users = [.. Enumerable.Range(0, 20).Select(i => $"user{i:00}")];

        string[] sessions = await Task.WhenAll(users.Select(async user =>
        {
            string id = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);
            await host.Workflow.StartRecoveryAsync(id, user, Ct);
            return id;
        }));

        var recoveryIds = new HashSet<string>();
        foreach (string id in sessions)
        {
            CallSession session = await host.SessionAsync(id);
            Assert.NotNull(session.RecoveryId);
            Assert.True(recoveryIds.Add(session.RecoveryId), "Two sessions share one recovery.");
        }
    }

    [Fact]
    public async Task StartRecovery_SameAccountFromTwoCalls_SecondGetsGenericCannotStart()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, TimeProvider.System);
        string a = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);
        string b = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);

        await host.Workflow.StartRecoveryAsync(a, "alex.morgan", Ct);
        ToolResult second = await host.Workflow.StartRecoveryAsync(b, "alex.morgan", Ct);

        Assert.Equal(ToolStatus.CannotStart, second.Status);
        Assert.Equal(Phrases.CannotStart, second.SayHint);
        Assert.Null((await host.SessionAsync(b)).RecoveryId);   // never attached to the other call's recovery
    }

    [Fact]
    public async Task SaveAsync_StaleCopyOfSession_ThrowsConflict()
    {
        var store = new InMemorySessionStore();
        await using var host = new AgentTestHost(store, new StatefulIssuerHandler(), TimeProvider.System);
        string id = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);
        CallSession copy1 = await host.SessionAsync(id);
        CallSession copy2 = await host.SessionAsync(id);

        copy1.TurnCount++;
        await store.SaveAsync(copy1, Ct);
        copy2.TurnCount++;

        await Assert.ThrowsAsync<SessionConflictException>(() => store.SaveAsync(copy2, Ct));
    }
}
```

`SaveAsync_StaleCopyOfSession_ThrowsConflict` assumes `InMemorySessionStore.GetAsync`
returns **copies** with their own ETag, as step 6's store contract tests require.
If step 6 already has this exact test in `SessionStoreContractTests`, delete this
one (no duplicates).
- [ ] **Step 2: Run, fix gaps in production code if needed, commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Reliability/ConcurrencyTests.cs
git commit -m "test(reliability): session isolation and concurrency"
```

---

### Task 4: Network failures and issuer outage

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Reliability/OutageTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Agent.Issuer;
using VoiceReset.Agent.Recovery;
using VoiceReset.Agent.Tests.Fakes;

namespace VoiceReset.Agent.Tests.Reliability;

public sealed class OutageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SubmitCode_NetworkFailureThenSameCode_IssuerSeesOneAttempt()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.AwaitingCode, issuer);
        issuer.FailNextWithNetworkError("/verify$");

        ToolResult first = await host.Workflow.SubmitCodeAsync(id, "111111", Ct);    // wrong code, answer lost
        ToolResult retry = await host.Workflow.SubmitCodeAsync(id, "111111", Ct);    // same code again

        Assert.False(first.Ok);
        Assert.NotEqual(ToolStatus.Verified, first.Status);
        Assert.Equal(ToolStatus.CodeIncorrect, retry.Status);   // still one try left: the retry reused the key (Q-6.1)
    }

    [Fact]
    public async Task CheckStatus_IssuerDownTwice_EscalatesDependencyUnavailableAndNeverClaimsSuccess()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.LinkSent, issuer);
        issuer.Down = true;

        ToolResult first = await host.Workflow.CheckResetStatusAsync(id, Ct);
        ToolResult second = await host.Workflow.CheckResetStatusAsync(id, Ct);

        Assert.NotEqual(ToolStatus.Completed, first.Status);
        Assert.NotEqual(ToolStatus.Completed, second.Status);
        CallSession session = await host.SessionAsync(id);
        Assert.Equal(TicketReasons.DependencyUnavailable, session.TicketReason);
    }

    [Fact]
    public async Task StartRecovery_IssuerDown_HonestMessageAndNoCodeSentClaim()
    {
        var issuer = new StatefulIssuerHandler { Down = true };
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);

        ToolResult result = await host.Workflow.StartRecoveryAsync(id, "alex.morgan", Ct);

        Assert.False(result.Ok);
        Assert.NotEqual(ToolStatus.CodeSent, result.Status);
        Assert.NotEqual(Phrases.CodeSent, result.SayHint);
    }
}
```

The exact status for "no answer" (`StatusUnknown` or `TryAgain`) is step 6's
choice. These tests only assert what must **never** happen (success claims) and
the ticket reason after two unknowns (Q-6.2).
- [ ] **Step 2: Run, fix, commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Reliability/OutageTests.cs
git commit -m "test(reliability): outages never become success"
```

---

### Task 5: Dropped calls in every state

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Reliability/DroppedCallTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Agent.Issuer;
using VoiceReset.Agent.Recovery;
using VoiceReset.Agent.Tests.Fakes;

namespace VoiceReset.Agent.Tests.Reliability;

public sealed class DroppedCallTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RecoveryState.AwaitingCode)]
    [InlineData(RecoveryState.Verified)]
    [InlineData(RecoveryState.LinkSent)]
    public async Task EndAsync_ConnectionLostMidRecovery_RecordsCallDroppedOnce(RecoveryState state)
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(state, issuer);

        await host.Workflow.EndAsync(id, CallEndReason.ConnectionLost, Ct);
        await host.Workflow.EndAsync(id, CallEndReason.ConnectionLost, Ct);   // idempotent

        CallSession session = await host.SessionAsync(id);
        Assert.Equal(TicketOutcomes.Cancelled, session.TicketOutcome);
        Assert.Equal(TicketReasons.CallDropped, session.TicketReason);
        Assert.Equal(CallEndReason.ConnectionLost, session.EndReason);
        Assert.Single(issuer.TicketFor(session.RecoveryId!).History);
    }

    [Fact]
    public async Task EndAsync_ConnectionLostBeforeAnyRecovery_NoTicket()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.AwaitingUsername, issuer);

        await host.Workflow.EndAsync(id, CallEndReason.ConnectionLost, Ct);

        CallSession session = await host.SessionAsync(id);
        Assert.Null(session.TicketId);
        Assert.Equal(0, issuer.Count("POST", "^/v1/tickets"));
    }

    [Fact]
    public async Task Reconcile_DroppedInLinkSentThenBrowserCompletes_BecomesResolved()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.LinkSent, issuer);
        await host.Workflow.EndAsync(id, CallEndReason.ConnectionLost, Ct);
        issuer.CompleteResetFor("alex.morgan", receipt: "rcpt-9", unlockStatus: "not_required");

        await host.Workflow.ReconcileAsync(id, Ct);

        Assert.Equal(TicketOutcomes.Resolved, (await host.SessionAsync(id)).TicketOutcome);
    }

    [Fact]
    public async Task Reconcile_HumanRequestedThenResetCompletes_KeepsEscalation()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.LinkSent, issuer);
        await host.Workflow.RequestHumanAsync(id, HumanRequestReasons.CallerAsked, Ct);
        issuer.CompleteResetFor("alex.morgan", receipt: "rcpt-7", unlockStatus: "not_required");

        await host.Workflow.ReconcileAsync(id, Ct);

        CallSession session = await host.SessionAsync(id);
        Assert.True(session.HumanRequested);
        Assert.Equal(TicketReasons.HumanRequested, session.TicketReason);   // contract: preserve the human request (Q-11.2)
    }
}
```

- [ ] **Step 2: Run, fix, commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Reliability/DroppedCallTests.cs
git commit -m "test(reliability): dropped calls record honest outcomes and reconcile"
```

---

### Task 6: Ambiguous and invalid speech

**Files:**
- Create: `tests/VoiceReset.Agent.Tests/Reliability/AmbiguousSpeechTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Agent.Recovery;
using VoiceReset.Agent.Tests.Fakes;

namespace VoiceReset.Agent.Tests.Reliability;

public sealed class AmbiguousSpeechTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("0 4 7")]
    [InlineData("four seven")]
    [InlineData("abc")]
    [InlineData("12345678901")]
    [InlineData("")]
    public async Task SubmitCode_UnusableInput_NoIssuerCallAndNoAttemptUsed(string spoken)
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.AwaitingCode, issuer);

        ToolResult result = await host.Workflow.SubmitCodeAsync(id, spoken, Ct);

        Assert.Equal(ToolStatus.InvalidCode, result.Status);
        Assert.Equal(0, issuer.Count("POST", "/verify$"));
        Assert.Equal(RecoveryState.AwaitingCode, await host.Workflow.GetStateAsync(id, Ct));
    }

    [Theory]
    [InlineData("oh four seven one nine two")]
    [InlineData("047 192")]
    [InlineData("0-4-7-1-9-2")]
    public async Task SubmitCode_SpokenVariantsOfTheRightCode_Verified(string spoken)
    {
        var issuer = new StatefulIssuerHandler();   // DefaultCode = "047192"
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.DriveToAsync(RecoveryState.AwaitingCode, issuer);

        ToolResult result = await host.Workflow.SubmitCodeAsync(id, spoken, Ct);

        Assert.Equal(ToolStatus.Verified, result.Status);
        Assert.Equal(1, issuer.Count("POST", "/verify$"));
    }

    [Theory]
    [InlineData("alex dot morgan")]
    [InlineData("Alex.Morgan ")]
    public async Task StartRecovery_SpokenUsernameVariants_Normalized(string spoken)
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);

        ToolResult result = await host.Workflow.StartRecoveryAsync(id, spoken, Ct);

        Assert.Equal(ToolStatus.CodeSent, result.Status);
        Assert.Equal("047192", issuer.CodeFor("alex.morgan"));   // the recovery exists for the normalized name
    }

    [Fact]
    public async Task StartRecovery_MarkupInsteadOfUsername_InvalidAndNoIssuerCall()
    {
        var issuer = new StatefulIssuerHandler();
        await using var host = new AgentTestHost(new InMemorySessionStore(), issuer, new FakeTimeProvider());
        string id = await host.Workflow.StartSessionAsync(CallChannel.Browser, Ct);

        ToolResult result = await host.Workflow.StartRecoveryAsync(id, "<script>", Ct);

        Assert.Equal(ToolStatus.InvalidUsername, result.Status);
        Assert.Equal(0, issuer.Count("POST", "^/v1/recoveries$"));
    }
}
```

Step 6's `InputNormalizerTests` may already cover some of these inputs at the unit
level. These tests are different on purpose: they prove the **issuer is never
called** and **no attempt is used**.
- [ ] **Step 2: Run, fix, commit**

```bash
git add solution/tests/VoiceReset.Agent.Tests/Reliability/AmbiguousSpeechTests.cs
git commit -m "test(reliability): ambiguous speech never burns verification attempts"
```

---

### Task 7: Mock issuer durability across a mocks restart

**Files:**
- Create: `tests/VoiceReset.Mocks.Tests/Reliability/IssuerDurabilityTests.cs`

- [ ] **Step 1: Write the test** using step 5's `MocksFactory` and `TestApi` helpers. Create **two** factories that share one `InMemoryMockStore` instance and one `FakeTimeProvider` (register both with `ConfigureTestServices`, replacing step 5's registrations). On factory A: start a recovery for `alex.morgan`, submit one wrong code (expect `422`, `attempts_remaining: 1`). Dispose A. On factory B:
  - `GET /v1/recoveries/{id}` shows `attempts_remaining: 1`;
  - `verification_expires_at` is unchanged (still 120 s after the **original** issuance);
  - a new `POST /v1/recoveries` for the same account (new `request_id`) returns `429 throttled`.

  Copy the request-building code from step 5's `VerifyTests` and `StartRecoveryRulesTests`, so the JSON and headers are exactly the contract's.
- [ ] **Step 2: Run, fix, commit**

```bash
git add solution/tests/VoiceReset.Mocks.Tests/Reliability/IssuerDurabilityTests.cs
git commit -m "test(mocks): issuer counters and expiry survive restarts"
```

---

### Task 8: Manual restart test on the isolated copy

- [ ] **Step 1:** Deploy the test copy ([step 13](step-13-deploy.md) Task 4 Step 1).
- [ ] **Step 2:** On the copy, talk to the agent until it says the link was sent.
- [ ] **Step 3:** Restart the copy's agent app **during** the call:

```powershell
az webapp restart -g rg-voicereset -n app-vr-agent-<suffix>t
```

Expected on the page: "Connection lost". A live call can't survive a process restart (Q-11.1).
- [ ] **Step 4:** Finish the reset in the browser form, using the link from the copy's inbox.
- [ ] **Step 5:** About 30 s after the app is back, check:
  - (a) the ticket is `resolved`/`reset_completed`. Query the copy's mocks storage account with `az storage entity query --account-name stvrmocks<suffix>t --table-name tickets --auth-mode login`, after giving yourself Storage Table Data Reader on that account for the test;
  - (b) a transcript stub with `turnsLost: true` exists (step 10).
- [ ] **Step 6:** Write `solution/docs/process/restart-test-evidence.md`: date, commit, steps, timings, results, and screenshots with no secrets or IDs.
- [ ] **Step 7: Commit**

```bash
git add solution/docs/process/restart-test-evidence.md
git commit -m "docs(process): manual restart test evidence"
```

---

### Task 9: Reliability evidence

**Files:**
- Create: `solution/docs/process/reliability-evidence.md`

- [ ] **Step 1: Write the mapping table**

| Assessed behaviour (README) | Mechanism | Automated evidence | Manual evidence |
|---|---|---|---|
| Ambiguous/invalid speech | `InputNormalizer`, read-back rule in the prompt | `AmbiguousSpeechTests`, step 12 scenario S22 | manual M03 |
| Timeouts | resilience handler; unknown → reconcile | `OutageTests` | — |
| Cancellation / dropped calls | `EndAsync`, ticket rules, reconciler | `DroppedCallTests` | manual M11 |
| Duplicate events | idempotency keys; `call_id` de-dup | `DuplicateEventTests`, step 5 idempotency tests | — |
| Concurrent sessions | per-connection session, ETag store, issuer throttle | `ConcurrencyTests` | manual M12 |
| Process restarts | Table store, `ReconciliationService` startup pass | `RestartTests`, `IssuerDurabilityTests` | `restart-test-evidence.md` |

Add the test counts from the `dotnet test` output, and every gap found and fixed in
this step (with the commit SHA).
- [ ] **Step 2:** Tick section F of `solution/docs/submission/requirements-checklist.md` item by item, **only** where the table shows evidence.
- [ ] **Step 3: Commit**

```bash
git add solution/docs/process/reliability-evidence.md solution/docs/submission/requirements-checklist.md
git commit -m "docs(process): reliability evidence and checklist"
```

---

## Self-review

- All six README reliability behaviours have tasks: speech (6), timeouts (4), drops (5), duplicates (2), concurrency (3), restarts (1, 7, 8) ✔
- Every name used comes from the step 6 plan (`ToolStatus.*`, `Phrases.*`, `TicketOutcomes.*`, `TicketReasons.*`, `CallEndReason.*`, `HumanRequestReasons.*`, `RecoveryWorkflow` methods) or is defined in Task 0 ✔
- Restart testing never touches the live app ✔

## Questions for the owner

- **Q-11.1:** A live call is dropped when its process restarts (unavoidable for an
  in-process voice session). **Default:** accept and document it. The state
  survives, and the ticket is reconciled.
- **Q-11.2:** If "human requested, then the reset completed" conflicts with step 6's
  choice, **default:** follow the contract (keep the escalation).
