using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoiceReset.Recovery;
using VoiceReset.Storage;

namespace VoiceReset.Tests.Recovery;

public sealed class OpenSessionCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Check_ResetCompletedWhileDown_ResolvesTicket()
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

        // Act: "restart" = a new app on the same storage, started a moment later
        await using var after = new RecoveryAppFactory(store);
        after.Time.Advance(TimeSpan.FromSeconds(1));
        await CheckAsync(after);

        // Assert
        var session = await after.SessionAsync(id, Ct);
        Assert.Equal(RecoveryState.Completed, session?.State);
        Assert.Equal("resolved", session?.TicketOutcome);
        Assert.NotNull(session?.ResetReceipt);
    }

    [Fact]
    public async Task Check_CallEndedByAgentAndLinkExpired_ClosesAsCallerCancelled()
    {
        // Arrange: the agent ended the call after the link was sent, then the process goes away
        var store = new InMemoryJsonStore();
        string id;
        await using (var before = new RecoveryAppFactory(store))
        {
            id = await before.StartRecoveryAsync("alex dot morgan", Ct);
            await before.Workflow.SubmitCodeAsync(id, await before.InboxCodeAsync(Ct), Ct);
            await before.Workflow.SendResetLinkAsync(id, Ct);
            await before.Workflow.EndCallAsync(id, "agent_ended", Ct);
        }

        // Act: the new process checks after the link has expired unused
        await using var after = new RecoveryAppFactory(store);
        after.Time.Advance(TimeSpan.FromSeconds(601));
        await CheckAsync(after);

        // Assert: closed, with the earlier end reason
        var session = await after.SessionAsync(id, Ct);
        Assert.Equal(RecoveryState.Cancelled, session?.State);
        Assert.Equal("agent_ended", session?.EndReason);
        Assert.Equal(("cancelled", "caller_cancelled"), (session?.TicketOutcome, session?.TicketReason));
    }

    // Time is advanced before Services is first used, so the check's process start time is the advanced time.
    private static Task CheckAsync(RecoveryAppFactory app) =>
        app.Services.GetServices<IHostedService>().OfType<OpenSessionCheck>().Single().CheckAsync(Ct);
}
