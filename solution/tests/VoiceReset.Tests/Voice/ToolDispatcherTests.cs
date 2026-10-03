using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VoiceReset.Recovery;
using VoiceReset.Tests.Recovery;
using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

public sealed class ToolDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DispatchAsync_SubmitCode_ReturnsTheWorkflowAnswer()
    {
        // Arrange: two fresh sessions in the same state; one goes through the dispatcher, one straight to the workflow
        await using var app = new RecoveryAppFactory();
        var dispatcher = app.Services.GetRequiredService<ToolDispatcher>();
        var viaDispatcher = await app.Workflow.StartSessionAsync("browser", Ct);
        var direct = await app.Workflow.StartSessionAsync("browser", Ct);

        // Act
        var result = await dispatcher.DispatchAsync(viaDispatcher, ToolDefinitions.SubmitCode, """{"code":"123456"}""", Ct);

        // Assert
        Assert.NotEqual(ToolDispatcher.InvalidArgumentStatus, result.Status);
        Assert.Equal(await app.Workflow.SubmitCodeAsync(direct, "123456", Ct), result);
    }

    [Theory]
    [InlineData("submit_code", "{not json")]
    [InlineData("submit_code", "\"123456\"")]
    [InlineData("submit_code", """{"code":"123456","session_id":"another"}""")]
    [InlineData("submit_code", """{"code":"123456","code":"654321"}""")]
    [InlineData("transfer_call", "{}")]
    public async Task DispatchAsync_BadJsonOrUnknownTool_ReturnsInvalidArgument(string tool, string arguments)
    {
        await using var app = new RecoveryAppFactory();
        var dispatcher = app.Services.GetRequiredService<ToolDispatcher>();
        var sessionId = await app.Workflow.StartSessionAsync("browser", Ct);

        var result = await dispatcher.DispatchAsync(sessionId, tool, arguments, Ct);

        Assert.False(result.Ok);
        Assert.Equal(ToolDispatcher.InvalidArgumentStatus, result.Status);
        Assert.Equal(RecoveryState.AwaitingUsername, await app.Workflow.GetStateAsync(sessionId, Ct));
    }

    [Fact]
    public async Task DispatchAsync_UnknownSession_ReturnsUnavailableInsteadOfThrowing()
    {
        await using var app = new RecoveryAppFactory();
        var dispatcher = app.Services.GetRequiredService<ToolDispatcher>();

        var result = await dispatcher.DispatchAsync("no-such-session", ToolDefinitions.CancelReset, "{}", Ct);

        Assert.Equal(new ToolResult(false, RecoveryWorkflow.UnavailableStatus, Phrases.NotAvailableNow), result);
    }

    [Fact]
    public async Task DispatchAsync_EndCall_ReturnsCallEndingAndEndsSession()
    {
        await using var app = new RecoveryAppFactory();
        var dispatcher = app.Services.GetRequiredService<ToolDispatcher>();
        var sessionId = await app.Workflow.StartSessionAsync("browser", Ct);

        var result = await dispatcher.DispatchAsync(sessionId, ToolDefinitions.EndCall, "{}", Ct);

        Assert.Equal(new ToolResult(true, ToolDispatcher.CallEndingStatus, Phrases.Goodbye), result);
        var session = await app.SessionAsync(sessionId, Ct);
        Assert.NotNull(session?.EndedAt);
        Assert.Equal(ToolDispatcher.AgentEndedReason, session?.EndReason);
    }

    [Fact]
    public async Task DispatchAsync_EndCallUnknownSession_StillReturnsCallEnding()
    {
        await using var app = new RecoveryAppFactory();
        var dispatcher = app.Services.GetRequiredService<ToolDispatcher>();

        var result = await dispatcher.DispatchAsync("no-such-session", ToolDefinitions.EndCall, "{}", Ct);

        Assert.Equal(new ToolResult(true, ToolDispatcher.CallEndingStatus, Phrases.Goodbye), result);
    }

    [Fact]
    public void ToOutputJson_Result_HasOkStatusAndSay()
    {
        var json = ToolDispatcher.ToOutputJson(new ToolResult(true, "link_sent", "The link's on its way."));

        var output = JsonDocument.Parse(json).RootElement;
        Assert.Equal(["ok", "status", "say"], output.EnumerateObject().Select(p => p.Name));
        Assert.True(output.GetProperty("ok").GetBoolean());
        Assert.Equal("link_sent", output.GetProperty("status").GetString());
        Assert.Equal("The link's on its way.", output.GetProperty("say").GetString());
        Assert.Contains("link's", json, StringComparison.Ordinal); // not escaped as '
    }
}
