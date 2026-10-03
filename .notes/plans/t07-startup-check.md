# T7 Startup Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After a process restart, settle the call sessions a previous process left open: a reset that completed meanwhile gets its `resolved` ticket; calls older than the call limit are closed as `cancelled/call_dropped`.

**Architecture:** `StartupCheck` runs once per process. It lists open sessions (`SessionStore.ListOpenAsync`) and calls a new `RecoveryWorkflow.ReconcileAsync` for each, so the same per-session lock and ticket rules from T6 apply. It is a `BackgroundService` (an `IHostedService`) that waits for `ApplicationStarted`: the issuer is reached over HTTP on this same app, so the server must be listening first, and startup is never blocked. Every error is caught and logged by type only.

**Tech Stack:** .NET 10 hosting, xUnit v3, `RecoveryAppFactory` from T6 (in-process mock, shared `InMemoryJsonStore`, `FakeTimeProvider`).

Depends on T6. Rules: CLAUDE.md 1, 3 (truth from the issuer: success only with a receipt), 4 (logs: IDs and types only). Commands run from `solution/`.

## Files

| File | Change |
|---|---|
| `src/VoiceReset/Voice/LimitsOptions.cs` | Create (contracts: `VoiceReset.Voice.LimitsOptions`, reused by T11) |
| `src/VoiceReset/Recovery/RecoveryWorkflow.cs` | Add `ReconcileAsync` |
| `src/VoiceReset/Recovery/StartupCheck.cs` | Create |
| `src/VoiceReset/Recovery/RecoveryServiceCollectionExtensions.cs` | Register `LimitsOptions` + `StartupCheck` |
| `appsettings.json` | `"Limits": { "MaxCallSeconds": 600 }` |
| `tests/VoiceReset.Tests/Recovery/StartupCheckTests.cs` | The restart test |

---

### Task 1: Settle open sessions on startup

- [ ] **Step 1: Write the failing test** — `tests/VoiceReset.Tests/Recovery/StartupCheckTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoiceReset.Recovery;
using VoiceReset.Storage;

namespace VoiceReset.Tests.Recovery;

public sealed class StartupCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartupCheck_ResetCompletedWhileDown_ResolvesTicket()
    {
        // Arrange: a call sends the link, the caller resets in the browser, then the process goes away
        var store = new InMemoryJsonStore();
        string id;
        await using (var before = new RecoveryAppFactory(store))
        {
            id = await before.StartRecoveryAsync("alex dot morgan", Ct);
            await before.Workflow.SubmitCodeAsync(id, await before.InboxCodeAsync(Ct), Ct);
            await before.Workflow.SendResetLinkAsync(id, Ct);
            await before.CompleteResetAsync(await before.InboxLinkAsync(Ct), Ct);
        }

        // Act: "restart" = a new app on the same storage
        await using var after = new RecoveryAppFactory(store);
        var check = after.Services.GetServices<IHostedService>().OfType<StartupCheck>().Single();
        Assert.NotNull(check.ExecuteTask);
        await check.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Assert
        var session = await after.SessionAsync(id, Ct);
        Assert.Equal(RecoveryState.Completed, session?.State);
        Assert.Equal("resolved", session?.TicketOutcome);
        Assert.NotNull(session?.ResetReceipt);
    }
}
```

- [ ] **Step 2: Run it, expect a compile failure** — `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Recovery.StartupCheckTests"` → `error CS0246: The type or namespace name 'StartupCheck' could not be found`.

- [ ] **Step 3: Create `src/VoiceReset/Voice/LimitsOptions.cs`** (skip if T11 already made it)

```csharp
using System.ComponentModel.DataAnnotations;

namespace VoiceReset.Voice;

public sealed class LimitsOptions
{
    public const string Section = "Limits";

    /// <summary>A call longer than this is ended politely; after a restart, older open sessions are closed.</summary>
    [Range(60, 3600)] public int MaxCallSeconds { get; set; } = 600;
}
```

- [ ] **Step 4: Add `ReconcileAsync` to `RecoveryWorkflow`** (same style as the T6 tool methods; uses the private `RunAsync`, `CompleteAsync`, `RecordOutcomeAsync`)

```csharp
    /// <summary>
    /// After a restart: records a reset that completed while no call was watching, and closes sessions older than
    /// maxCallAge as cancelled/call_dropped. Returns true when the session was settled.
    /// </summary>
    public Task<bool> ReconcileAsync(string sessionId, TimeSpan maxCallAge, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (!session.IsOpen) { return false; }
        if (session.RecoveryId is not null)
        {
            var status = await issuer.GetRecoveryAsync(session.RecoveryId, ct);
            if (status.Outcome == IssuerOutcome.Unavailable) { return false; }   // unknown: leave it for the next start
            if (status.Value is { Status: "completed", ResetReceipt: { } receipt } completed)
            {
                await CompleteAsync(session, receipt, completed.UnlockStatus, ct);
                return true;
            }
        }
        if (time.GetUtcNow() - session.StartedAt < maxCallAge) { return false; }
        session.EndedAt ??= time.GetUtcNow();
        session.EndReason ??= "restart";
        session.State = RecoveryState.Cancelled;
        await RecordOutcomeAsync(session, "cancelled", "call_dropped", null, ct);   // no-op without a recovery
        return true;
    }, ct);
```

- [ ] **Step 5: Create `src/VoiceReset/Recovery/StartupCheck.cs`**

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Voice;

namespace VoiceReset.Recovery;

/// <summary>
/// Once per process start: settles sessions a previous process left open (README: process restarts).
/// Runs after ApplicationStarted because the issuer is called over HTTP on this same app; never fails startup.
/// </summary>
public sealed partial class StartupCheck(
    SessionStore sessions, RecoveryWorkflow workflow, IOptions<LimitsOptions> limits,
    IHostApplicationLifetime lifetime, ILogger<StartupCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WhenStartedAsync(stoppingToken);
            var open = await sessions.ListOpenAsync(stoppingToken);
            var maxCallAge = TimeSpan.FromSeconds(limits.Value.MaxCallSeconds);
            var settled = 0;
            foreach (var session in open)
            {
                try
                {
                    if (await workflow.ReconcileAsync(session.SessionId, maxCallAge, stoppingToken)) { settled++; }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSessionFailed(logger, session.SessionId, ex.GetType().Name);
                }
            }
            LogDone(logger, open.Count, settled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex.GetType().Name);   // e.g. storage unreachable: the app still starts
        }
    }

    private async Task WhenStartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup check: {OpenCount} open sessions, {SettledCount} settled")]
    private static partial void LogDone(ILogger logger, int openCount, int settledCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup check failed for session {SessionId}: {ErrorType}")]
    private static partial void LogSessionFailed(ILogger logger, string sessionId, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup check failed: {ErrorType}")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
```

Only the exception type is logged: messages can carry URLs or response text, and rule 4 allows IDs, states and types only.

- [ ] **Step 6: Register** — in `AddRecovery()` (`RecoveryServiceCollectionExtensions.cs`) add, before `return services;`:

```csharp
        services.AddOptions<LimitsOptions>().BindConfiguration(LimitsOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddHostedService<StartupCheck>();
```

and `using VoiceReset.Voice;` at the top. In `appsettings.json` add `"Limits": { "MaxCallSeconds": 600 }`.

- [ ] **Step 7: Run the test** — `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Recovery.StartupCheckTests"` → `Test run summary: Passed!`, `total: 1`, `failed: 0`.

- [ ] **Step 8: Full check** — `dotnet build VoiceReset.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`; `dotnet test --project tests/VoiceReset.Tests` → all pass (the T6 apps now also run the check; it can only settle a session the same way the tools would, under the same lock, so those tests are unaffected).

- [ ] **Step 9: Commit**

```bash
git add solution
git commit -m "feat: settle open call sessions on startup"
```

---

## Self-review

- Spec: once at startup; open sessions with a recovery → GET status; `completed` + receipt → `resolved` (via T6 `CompleteAsync`, so the session also becomes `Completed`); else older than `Limits:MaxCallSeconds` → ended + `cancelled/call_dropped`; issuer unavailable → left open; errors logged by type, never crash startup. Test: restart with a shared `InMemoryJsonStore`.
- Types: `ReconcileAsync` uses only T6 members (`RunAsync`, `CompleteAsync`, `RecordOutcomeAsync`, `issuer`, `time`); `LimitsOptions` matches the contracts table.

## Questions

1. The test needs the T3 mock to reload `mock/state.json` from the store in a new process (the recovery's receipt must survive the "restart"). Confirm T3 loads state on first use.
2. Sessions without a recovery that are older than the limit are also closed (no ticket, since none exists). OK?
3. A session younger than `MaxCallSeconds` at startup stays open until the next restart (no periodic re-check). Enough, or add one more check after `MaxCallSeconds`?
4. A dropped call whose link is still valid is closed after 10 minutes as `cancelled/call_dropped`; a reset completed after that is not reconciled. Document as a known limitation?
5. Single instance assumed: with several instances, each would run the check (harmless: ticket updates are idempotent, but "settled" counts would double).

## Additions to contracts

- `RecoveryWorkflow.ReconcileAsync(string sessionId, TimeSpan maxCallAge, CancellationToken ct) : Task<bool>`.
- `StartupCheck` is a `BackgroundService` (still an `IHostedService`), registered by `AddRecovery()`; it also takes `IOptions<LimitsOptions>`, `IHostApplicationLifetime`, `ILogger<StartupCheck>`.
- `LimitsOptions` is created and registered by T7 (`AddRecovery()`); T11 reuses it and must not register it again.
- `CallSession.EndReason = "restart"` for sessions closed by the startup check.
