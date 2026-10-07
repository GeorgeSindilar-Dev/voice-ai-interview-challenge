using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoiceReset.Features.Mock;
using VoiceReset.Features.Recovery;

namespace VoiceReset.Tests.Features.Recovery;

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
        var spokenCode = string.Join(' ', (await app.InboxCodeAsync(Ct)).AsEnumerable()); // "0 4 7 1 1 2"
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
        Assert.Equal(Phrases.CodeIncorrect, wrong.Say); // one try left: the fragment used no attempt
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

        var second = await app.StartRecoveryAsync(Spoken, Ct); // throttled by the issuer: no recovery
        var secondLink = await app.Workflow.SendResetLinkAsync(second, Ct);

        Assert.Equal(RecoveryState.AwaitingUsername, await app.Workflow.GetStateAsync(second, Ct));
        Assert.Equal("not_allowed", secondLink.Status);
        Assert.Equal(RecoveryState.Verified, await app.Workflow.GetStateAsync(first, Ct));
    }

    [Fact]
    public async Task EndCall_AgentEndedThenDropped_RecordsCallerCancelledOnce()
    {
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);

        await app.Workflow.EndCallAsync(id, "agent_ended", Ct);
        await app.Workflow.EndCallAsync(id, "call_dropped", Ct);

        var session = await app.SessionAsync(id, Ct);
        Assert.Equal(("cancelled", "caller_cancelled", "agent_ended"), (session?.TicketOutcome, session?.TicketReason, session?.EndReason));
    }

    [Fact]
    public async Task SubmitCode_EarlierAnswerLost_MovesOnAsVerified()
    {
        // Arrange: the issuer accepted the code, but the answer never reached the call (simulated with another key)
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);
        var code = await app.InboxCodeAsync(Ct);
        var recoveryId = (await app.SessionAsync(id, Ct))?.RecoveryId ?? "";
        await app.Services.GetRequiredService<MockIssuer>().VerifyAsync(recoveryId, "lost-answer", new VerifyRequest(code), Ct);

        // Act: the caller reads the code again; the issuer answers invalid_state (already verified)
        var result = await app.Workflow.SubmitCodeAsync(id, code, Ct);

        // Assert
        Assert.Equal("verified", result.Status);
        Assert.Equal(RecoveryState.Verified, await app.Workflow.GetStateAsync(id, Ct));
    }

    [Fact]
    public async Task EndCall_LinkOutThenResetInBrowser_TicketPendingThenResolved()
    {
        // Arrange: the link is sent, then the caller hangs up before using it
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);
        await app.Workflow.SubmitCodeAsync(id, await app.InboxCodeAsync(Ct), Ct);
        await app.Workflow.SendResetLinkAsync(id, Ct);
        await app.Workflow.EndCallAsync(id, "call_dropped", Ct);
        var afterHangUp = await app.SessionAsync(id, Ct);

        // Act: the caller finishes the form, then the open session check runs
        await app.CompleteResetAsync(await app.InboxLinkAsync(Ct), Ct);
        await app.Services.GetServices<IHostedService>().OfType<OpenSessionCheck>().Single().CheckAsync(Ct);

        // Assert
        Assert.Equal(("pending", "completion_unknown"), (afterHangUp?.TicketOutcome, afterHangUp?.TicketReason));
        var settled = await app.SessionAsync(id, Ct);
        Assert.Equal((RecoveryState.Completed, "resolved"), (settled?.State, settled?.TicketOutcome));
    }

    [Fact]
    public async Task Cancel_AfterLinkThenResetInBrowser_TicketResolvedByTheCheck()
    {
        // Arrange: the caller cancels after the link was sent and ends the call, then uses the link anyway
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);
        await app.Workflow.SubmitCodeAsync(id, await app.InboxCodeAsync(Ct), Ct);
        await app.Workflow.SendResetLinkAsync(id, Ct);
        await app.Workflow.CancelAsync(id, Ct);
        await app.Workflow.EndCallAsync(id, "agent_ended", Ct);
        await app.CompleteResetAsync(await app.InboxLinkAsync(Ct), Ct);

        // Act
        await app.Services.GetServices<IHostedService>().OfType<OpenSessionCheck>().Single().CheckAsync(Ct);

        // Assert
        var session = await app.SessionAsync(id, Ct);
        Assert.Equal((RecoveryState.Completed, "resolved"), (session?.State, session?.TicketOutcome));
    }

    [Fact]
    public async Task ReportNoBrowser_AfterRecoveryStarted_EscalatesAsBrowserUnavailable()
    {
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);

        var result = await app.Workflow.ReportNoBrowserAsync(id, Ct);

        Assert.Equal(new ToolResult(true, "escalated", $"{Phrases.NoBrowser} {Phrases.TicketCreated}"), result);
        var session = await app.SessionAsync(id, Ct);
        Assert.Equal((RecoveryState.Escalated, "escalated", "browser_unavailable"), (session?.State, session?.TicketOutcome, session?.TicketReason));
    }

    [Fact]
    public async Task CheckResetStatus_HumanRequestedAfterLinkThenReset_SaysSuccessAndKeepsEscalation()
    {
        // Arrange: the caller asks for a person after the link was sent, then finishes the form anyway
        await using var app = new RecoveryAppFactory();
        var id = await app.StartRecoveryAsync(Spoken, Ct);
        await app.Workflow.SubmitCodeAsync(id, await app.InboxCodeAsync(Ct), Ct);
        await app.Workflow.SendResetLinkAsync(id, Ct);
        await app.Workflow.RequestHumanAsync(id, Ct);
        await app.CompleteResetAsync(await app.InboxLinkAsync(Ct), Ct);

        // Act
        var result = await app.Workflow.CheckResetStatusAsync(id, Ct);

        // Assert: the true result is said; the human-requested escalation stays on the ticket
        Assert.Equal(Phrases.Completed, result.Say);
        var session = await app.SessionAsync(id, Ct);
        Assert.Equal((RecoveryState.Completed, "escalated"), (session?.State, session?.TicketOutcome));
        Assert.NotNull(session?.ResetReceipt);
    }

    private static string WrongCode(string code) => code == "000000" ? "111111" : "000000";
}
