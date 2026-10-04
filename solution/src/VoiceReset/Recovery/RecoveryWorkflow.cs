using System.Collections.Concurrent;

namespace VoiceReset.Recovery;

/// <summary>The backend decides: each tool is checked against this call's state; outcomes come only from the issuer.</summary>
public sealed partial class RecoveryWorkflow(SessionStore sessions, IssuerClient issuer, TimeProvider time, ILogger<RecoveryWorkflow> logger)
{
    /// <summary>The status of every "the service isn't responding" result.</summary>
    public const string UnavailableStatus = "unavailable";

    private const string AgentEndedReason = "agent_ended";
    private const string Resolved = "resolved";
    private const string Escalated = "escalated";
    private const string Cancelled = "cancelled";
    private const string Pending = "pending";
    private const string CompletionUnknownReason = "completion_unknown";
    private const string RestartReason = "restart";

    private static readonly ToolResult s_notAllowed = new(false, "not_allowed", Phrases.NotAllowed);
    private static readonly ToolResult s_unavailable = new(false, UnavailableStatus, Phrases.NotAvailableNow);
    private static readonly ToolResult s_noTicket = new(false, "no_ticket", $"{Phrases.HumanRequested} {Phrases.TicketNotCreated}");
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
        if (session.State != RecoveryState.AwaitingUsername)
        {
            return s_notAllowed;
        }
        if (SpokenInput.Username(spokenUsername) is not { } username)
        {
            return new(false, "invalid_username", Phrases.UsernameUnclear);
        }
        if (session.RequestId is null || session.Username != username)
        {
            session.Username = username;
            session.RequestId = NewId();
            await sessions.SaveAsync(session, ct);
        }
        var result = await issuer.StartRecoveryAsync(username, session.RequestId, ct);
        if (result.Outcome == IssuerOutcome.Unavailable)
        {
            return s_unavailable; // the recovery may have started: keep the RequestId so a retry can't start a second one
        }
        if (result.Value is null)
        {
            session.RequestId = null; // refused (e.g. throttled): the next attempt is a new request
            return new(false, "not_started", Phrases.CantStartNow);
        }
        session.RecoveryId = result.Value.RecoveryId;
        session.State = RecoveryState.AwaitingCode;
        await EnsureTicketAsync(session, ct);
        return new(true, "code_sent", Phrases.CodeSent);
    }, ct);

    public Task<ToolResult> SubmitCodeAsync(string sessionId, string? spokenCode, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session is not { State: RecoveryState.AwaitingCode, RecoveryId: { } recoveryId })
        {
            return s_notAllowed;
        }
        if (SpokenInput.Code(spokenCode) is not { } code)
        {
            return new(false, "invalid_code", Phrases.CodeUnclear);
        }
        // Kept after a timeout so a retry of that submission can't count twice; any answer (also
        // idempotency_conflict when the caller now reads a different code) clears it for the next one.
        session.VerifyKey ??= NewId();
        await sessions.SaveAsync(session, ct);
        var result = await issuer.VerifyAsync(recoveryId, code, session.VerifyKey, ct);
        if (result.Outcome != IssuerOutcome.Unavailable)
        {
            session.VerifyKey = null;
        }
        // invalid_state or idempotency_conflict can mean an earlier submission was accepted but its answer was lost.
        if (result.Outcome == IssuerOutcome.Success
            || (result.ErrorCode is "invalid_state" or "idempotency_conflict" && await IsVerifiedAsync(recoveryId, ct)))
        {
            session.State = RecoveryState.Verified;
            return new(true, "verified", Phrases.Verified);
        }
        return result.ErrorCode switch
        {
            "verification_failed" => new(false, "code_incorrect", Phrases.CodeIncorrect),
            "verification_exhausted" => await EscalateAsync(
                session, reason: "verification_exhausted", ok: false, status: "exhausted", firstSentence: Phrases.Exhausted, ct),
            "recovery_expired" => await EscalateAsync(
                session, reason: "verification_expired", ok: false, status: "expired", firstSentence: Phrases.Expired, ct),
            _ => s_unavailable,
        };
    }, ct);

    public Task<ToolResult> SendResetLinkAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session is not { State: RecoveryState.Verified, RecoveryId: { } recoveryId })
        {
            return s_notAllowed;
        }
        session.LinkOperationId ??= NewId();
        await sessions.SaveAsync(session, ct);
        var result = await issuer.SendResetLinkAsync(recoveryId, session.LinkOperationId, ct);
        if (result.ErrorCode == "recovery_expired")
        {
            return await EscalateAsync(
                session, reason: "verification_expired", ok: false, status: "expired", firstSentence: Phrases.Expired, ct);
        }
        if (result.Value is null)
        {
            return s_unavailable;
        }
        session.State = RecoveryState.LinkSent;
        return new(true, "link_sent", Phrases.LinkSent);
    }, ct);

    public Task<ToolResult> CheckResetStatusAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session.State == RecoveryState.Completed)
        {
            return CompletedResult(session.UnlockStatus);
        }
        if (session is not { State: RecoveryState.LinkSent, RecoveryId: { } recoveryId })
        {
            return s_notAllowed;
        }
        var result = await issuer.GetRecoveryAsync(recoveryId, ct);
        return result.Value switch
        {
            { Status: "completed", ResetReceipt: { } receipt } status => await CompleteAsync(session, receipt, status.UnlockStatus, ct),
            { Status: "reset_failed" } => await EscalateAsync(
                session, reason: "dependency_unavailable", ok: false, status: "reset_failed", firstSentence: Phrases.ResetFailed, ct),
            { Status: "expired" } => await EscalateAsync(
                session, reason: "browser_unavailable", ok: false, status: "expired", firstSentence: Phrases.LinkExpired, ct),
            _ => new(false, "not_confirmed", Phrases.CantConfirmYet), // link unused, pending, or issuer unavailable
        };
    }, ct);

    public Task<ToolResult> RequestHumanAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session => session.State switch
    {
        RecoveryState.Completed or RecoveryState.Cancelled => s_notAllowed,
        RecoveryState.Escalated => new(true, "escalated", TicketSentence(session)),
        RecoveryState.AwaitingUsername => s_noTicket, // no recovery, so no ticket: the caller can still start a reset
        _ => await EscalateAsync(
            session, reason: "human_requested", ok: true, status: "escalated", firstSentence: Phrases.HumanRequested, ct),
    }, ct);

    public Task<ToolResult> CancelAsync(string sessionId, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (!session.IsOpen)
        {
            return s_notAllowed;
        }
        var linkSent = session.State == RecoveryState.LinkSent;
        session.State = RecoveryState.Cancelled;
        await RecordOutcomeAsync(session, Cancelled, "caller_cancelled", null, ct);
        return new(true, "cancelled", linkSent ? Phrases.CancelledAfterLink : Phrases.Cancelled);
    }, ct);

    /// <summary>
    /// Idempotent. reason: agent_ended (cancelled/caller_cancelled), anything else (cancelled/call_dropped).
    /// With a link out, the caller may still finish the form: the ticket is resolved if the reset is already done,
    /// otherwise pending/completion_unknown until ReconcileAsync settles it. The state stays open for that.
    /// </summary>
    public Task EndCallAsync(string sessionId, string reason, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        if (session.EndedAt is not null)
        {
            return false; // the bool only satisfies RunAsync<T>: true = this call ended the session
        }
        session.EndedAt = time.GetUtcNow();
        session.EndReason = reason;
        if (session is { State: RecoveryState.LinkSent, RecoveryId: { } recoveryId })
        {
            var status = await issuer.GetRecoveryAsync(recoveryId, ct);
            if (status.Value is { Status: "completed", ResetReceipt: { } receipt } completed)
            {
                await CompleteAsync(session, receipt, completed.UnlockStatus, ct);
                return true;
            }
            await RecordOutcomeAsync(session, Pending, CompletionUnknownReason, null, ct);
            return true;
        }
        if (session.IsOpen) // a final session already has its final ticket outcome
        {
            await RecordOutcomeAsync(session, Cancelled, CancelReason(reason), null, ct);
        }
        return true;
    }, ct);

    /// <summary>
    /// For open sessions without a live call (ended, or started before noLiveCallBefore): records a reset that
    /// completed while no call was watching, waits while a link can still be used, then closes the session as
    /// cancelled. Returns true when the session was settled.
    /// </summary>
    public Task<bool> ReconcileAsync(string sessionId, DateTimeOffset noLiveCallBefore, CancellationToken ct) => RunAsync(sessionId, async session =>
    {
        var callOver = session.EndedAt is not null || session.StartedAt < noLiveCallBefore;
        if (!session.IsOpen || !callOver)
        {
            return false; // a live call settles itself
        }
        if (session.RecoveryId is not null)
        {
            var status = await issuer.GetRecoveryAsync(session.RecoveryId, ct);
            if (status.Outcome == IssuerOutcome.Unavailable || status.Value?.Status == "link_issued")
            {
                return false; // unknown, or the caller can still use the link: try again next time
            }
            if (status.Value is { Status: "completed", ResetReceipt: { } receipt } completed)
            {
                await CompleteAsync(session, receipt, completed.UnlockStatus, ct);
                return true;
            }
        }
        session.EndedAt ??= time.GetUtcNow();
        session.EndReason ??= RestartReason;
        session.State = RecoveryState.Cancelled;
        if (session.TicketOutcome != Cancelled) // EndCallAsync may have recorded it already
        {
            await RecordOutcomeAsync(session, Cancelled, CancelReason(session.EndReason), null, ct); // no ticket without a recovery
        }
        return true;
    }, ct);

    private static string CancelReason(string? endReason) => endReason == AgentEndedReason ? "caller_cancelled" : "call_dropped";

    private async Task<ToolResult> CompleteAsync(CallSession session, string receipt, string? unlockStatus, CancellationToken ct)
    {
        session.State = RecoveryState.Completed;
        session.ResetReceipt = receipt;
        session.UnlockStatus = unlockStatus;
        await RecordOutcomeAsync(session, Resolved, "reset_completed", receipt, ct);
        return CompletedResult(unlockStatus);
    }

    private async Task<bool> IsVerifiedAsync(string recoveryId, CancellationToken ct) =>
        (await issuer.GetRecoveryAsync(recoveryId, ct)).Value?.Status == "verified";

    private static ToolResult CompletedResult(string? unlockStatus) =>
        new(true, "completed", unlockStatus == "unlocked" ? Phrases.CompletedWithUnlock : Phrases.Completed);

    private async Task<ToolResult> EscalateAsync(CallSession session, string reason, bool ok, string status, string firstSentence, CancellationToken ct)
    {
        session.State = RecoveryState.Escalated;
        await RecordOutcomeAsync(session, Escalated, reason, null, ct);
        return new(ok, status, $"{firstSentence} {TicketSentence(session)}");
    }

    // "A ticket was created" is said only when the issuer created one (even if a later outcome update failed).
    private static string TicketSentence(CallSession session) => session.TicketId is not null ? Phrases.TicketCreated : Phrases.TicketNotCreated;

    // Created right after a recovery starts, so any later failure can be escalated.
    private async Task EnsureTicketAsync(CallSession session, CancellationToken ct)
    {
        if (session.TicketId is not null || session.RecoveryId is null)
        {
            return;
        }
        session.TicketOperationId ??= NewId();
        await sessions.SaveAsync(session, ct);
        var ticket = await issuer.CreateTicketAsync(session.RecoveryId, session.TicketOperationId, ct);
        if (ticket.Value is null)
        {
            LogTicketFailed(logger, session.SessionId, "create");
            return;
        }
        session.TicketId = ticket.Value.TicketId;
        session.TicketOutcome = ticket.Value.Outcome;
    }

    // Keeps what the issuer returned: a human-requested escalation stays escalated whatever was asked.
    private async Task RecordOutcomeAsync(CallSession session, string outcome, string reason, string? receipt, CancellationToken ct)
    {
        await EnsureTicketAsync(session, ct);
        if (session.TicketId is null || (session.TicketOutcome == outcome && session.TicketReason == reason))
        {
            return;
        }
        session.OutcomeOperationId = NewId();
        await sessions.SaveAsync(session, ct);
        var result = await issuer.SetTicketOutcomeAsync(session.TicketId, outcome, receipt, reason, session.OutcomeOperationId, ct);
        if (result.Value is null)
        {
            LogTicketFailed(logger, session.SessionId, outcome);
            return;
        }
        session.TicketOutcome = result.Value.Outcome;
        session.TicketReason = result.Value.ReasonCode;
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
