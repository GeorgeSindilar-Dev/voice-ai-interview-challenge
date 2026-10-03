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

    [Fact]
    public async Task StartupCheck_CallEndedByAgentLongAgo_KeepsCallerCancelledTicket()
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

        // Act: the new process starts after the call limit has passed
        await using var after = new RecoveryAppFactory(store);
        after.Time.Advance(TimeSpan.FromSeconds(601));
        var check = after.Services.GetServices<IHostedService>().OfType<StartupCheck>().Single();
        Assert.NotNull(check.ExecuteTask);
        await check.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Assert: closed, but the earlier end reason and ticket outcome stay
        var session = await after.SessionAsync(id, Ct);
        Assert.Equal(RecoveryState.Cancelled, session?.State);
        Assert.Equal("agent_ended", session?.EndReason);
        Assert.Equal("cancelled", session?.TicketOutcome);
        Assert.Equal("caller_cancelled", session?.TicketReason);
    }
}
