namespace VoiceReset.Mock;

public sealed partial class MockIssuer
{
    private static readonly string[] s_safeReasons = ["browser_unavailable", "verification_exhausted", "verification_expired",
        MockTicket.HumanRequested, "caller_cancelled", "call_dropped", "dependency_unavailable", "completion_unknown"];

    public Task<MockResult> CreateTicketAsync(CreateTicketRequest request, CancellationToken ct) => RunAsync(_ =>
        !_state.Recoveries.ContainsKey(request.RecoveryId) ? MockResult.NotFound()
        : WithReplay($"ticket:{request.OperationId}", Hash(request.RecoveryId), () => CreateTicket(request.RecoveryId)), save: true, ct);

    public Task<MockResult> SetTicketOutcomeAsync(string ticketId, TicketOutcomeRequest request, CancellationToken ct) => RunAsync(now =>
        !_state.Tickets.TryGetValue(ticketId, out var ticket) ? MockResult.NotFound()
        : WithReplay($"outcome:{ticketId}:{request.OperationId}", Hash($"{request.Outcome}|{request.ResetReceipt}|{request.ReasonCode}"),
            () => ApplyOutcome(ticket, request, now)), save: true, ct);

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
        if (request.Outcome == MockTicket.Resolved)
        {
            // Resolved needs the issuer's receipt for this ticket's recovery.
            var hasReceipt = request.ReasonCode == MockTicket.ResetCompleted && request.ResetReceipt is { } receipt
                && _state.Recoveries.TryGetValue(ticket.RecoveryId, out var recovery) && recovery.ResetReceipt == receipt;
            if (!hasReceipt)
            {
                return MockResult.InvalidState();
            }
        }
        else if (!IsSafeNonResolution(request))
        {
            return MockResult.InvalidRequest();
        }
        // A human-requested escalation is kept against any later outcome: an automated result does not fulfil that request.
        if (ticket is { Outcome: MockTicket.Escalated, ReasonCode: MockTicket.HumanRequested })
        {
            return Outcome(ticket);
        }
        if (ticket.Outcome == MockTicket.Resolved && request.Outcome != MockTicket.Resolved) // a confirmed success is never overwritten
        {
            return MockResult.InvalidState();
        }
        (ticket.Outcome, ticket.ResetReceipt, ticket.ReasonCode) = (request.Outcome, request.ResetReceipt, request.ReasonCode);
        ticket.History.Add(new TicketUpdate(now, request.OperationId, request.Outcome, request.ReasonCode));
        return Outcome(ticket);
    }

    private static MockResult Outcome(MockTicket ticket) =>
        MockResult.Json(200, new TicketOutcomeResponse(ticket.Id, ticket.RecoveryId, ticket.Outcome, ticket.ResetReceipt, ticket.ReasonCode));

    private static bool IsSafeNonResolution(TicketOutcomeRequest request) =>
        request.Outcome is MockTicket.Escalated or MockTicket.Cancelled or MockTicket.Pending
        && request.ResetReceipt is null
        && s_safeReasons.Contains(request.ReasonCode);
}
