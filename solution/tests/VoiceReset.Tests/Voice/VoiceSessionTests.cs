using Azure.AI.VoiceLive;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReset.Recovery;
using VoiceReset.Transcripts;
using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

/// <summary>The real workflow and dispatcher (in-memory store); the fakes play Voice Live and the caller.</summary>
public sealed class VoiceSessionTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncDisposable
{
    private readonly CallLog _log = new();
    private readonly FakeVoiceLiveConnection _voiceLive;
    private readonly FakeAudioChannel _caller;
    private readonly IServiceScope _scope;
    private readonly VoiceSession _session;
    private Task _run = Task.CompletedTask;

    public VoiceSessionTests(WebApplicationFactory<Program> factory)
    {
        _voiceLive = new FakeVoiceLiveConnection(_log);
        _caller = new FakeAudioChannel(_log);
        _scope = factory.Services.CreateScope();
        _session = new VoiceSession(
            _scope.ServiceProvider.GetRequiredService<RecoveryWorkflow>(),
            _scope.ServiceProvider.GetRequiredService<ToolDispatcher>(),
            _voiceLive, _caller, new NullTranscriptWriter(),
            new VoiceLiveOptions { Endpoint = "https://voicelive.invalid/" }, new LimitsOptions(),
            TimeProvider.System, NullLogger<VoiceSession>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _caller.HangUp();   // every test ends its call
        await _run.WaitAsync(TimeSpan.FromSeconds(5));
        _session.Dispose();
        _scope.Dispose();
    }

    [Fact]
    public async Task RunAsync_SessionUpdated_RequestsTheGreetingFirst()
    {
        await StartAndGreetAsync();

        var commands = _log.Entries.Where(e => !e.StartsWith("read:", StringComparison.Ordinal));
        Assert.Equal(["configure", $"response:{VoiceSession.GreetingInstruction}"], commands);
    }

    [Fact]
    public async Task RunAsync_FunctionCall_AsksForTheNextResponseOnlyAfterResponseDone()
    {
        // Arrange: the greeting finishes
        await StartAndGreetAsync();
        _voiceLive.Emit(ResponseCreated());
        _voiceLive.Emit(ResponseDone());

        // Act: the model calls a tool inside a response that is still active
        _voiceLive.Emit(ResponseCreated());
        _voiceLive.Emit(VoiceLiveModelFactory.SessionUpdateResponseFunctionCallArgumentsDone(
            callId: "call-1", arguments: """{"code":"123456"}""", name: ToolDefinitions.SubmitCode));
        await _voiceLive.SyncAsync();
        var requestsBeforeDone = ResponsesRequested();
        _voiceLive.Emit(ResponseDone());
        await _voiceLive.SyncAsync();

        // Assert
        Assert.Contains("output:call-1", _log.Entries);
        Assert.Equal(1, requestsBeforeDone);     // only the greeting
        Assert.Equal(2, ResponsesRequested());   // the answer to the tool result
    }

    [Fact]
    public async Task RunAsync_SpeechStarted_StopsPlayback()
    {
        await StartAndGreetAsync();

        _voiceLive.Emit(VoiceLiveModelFactory.SessionUpdateInputAudioBufferSpeechStarted(audioStartMs: 100, itemId: "item-1"));
        await _voiceLive.SyncAsync();

        Assert.Contains("stop-playback", _log.Entries);
    }

    [Fact]
    public async Task RunAsync_CallerDisconnects_EndsTheCallAsDropped()
    {
        await StartAndGreetAsync();

        _caller.HangUp();
        await _run.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        var stored = await _scope.ServiceProvider.GetRequiredService<SessionStore>().GetAsync(_session.SessionId, Ct);
        Assert.Equal(VoiceSession.CallDroppedReason, stored?.EndReason);
        Assert.Contains($"ended:{VoiceSession.CallDroppedReason}", _log.Entries);
    }

    [Fact]
    public async Task RunAsync_EndCallTool_SaysGoodbyeAfterResponseDoneThenEnds()
    {
        // Arrange: the greeting finishes, then the model calls end_call inside an active response
        await StartAndGreetAsync();
        _voiceLive.Emit(ResponseCreated());
        _voiceLive.Emit(ResponseDone());
        _voiceLive.Emit(ResponseCreated());
        _voiceLive.Emit(VoiceLiveModelFactory.SessionUpdateResponseFunctionCallArgumentsDone(
            callId: "call-1", arguments: "{}", name: ToolDefinitions.EndCall));
        await _voiceLive.SyncAsync();
        var saidBeforeDone = _log.Entries.Any(e => e.StartsWith("say:", StringComparison.Ordinal));

        // Act: the tool's response finishes, then the goodbye finishes
        _voiceLive.Emit(ResponseDone());
        await _voiceLive.SyncAsync();
        var saidGoodbye = _log.Entries.Contains($"say:{Phrases.Goodbye}");
        _voiceLive.Emit(ResponseDone());
        await _run.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        // Assert
        Assert.False(saidBeforeDone);
        Assert.True(saidGoodbye);
        Assert.Contains($"ended:{ToolDispatcher.AgentEndedReason}", _log.Entries);
    }

    [Fact]
    public async Task RunAsync_FilteredResponse_SaysSafeLineOnce()
    {
        await StartAndGreetAsync();

        _voiceLive.Emit(ResponseCreated());
        _voiceLive.Emit(FailedResponseDone());   // the greeting fails: the safe line
        _voiceLive.Emit(ResponseCreated());
        _voiceLive.Emit(FailedResponseDone());   // the safe line fails too: not repeated
        await _voiceLive.SyncAsync();

        Assert.Single(_log.Entries, e => e == $"say:{VoiceSession.SafeLine}");
    }

    private async Task StartAndGreetAsync()
    {
        _run = _session.RunAsync(Ct);
        await _log.WaitUntilAsync(entries => entries.Contains("configure"));
        _voiceLive.Emit(VoiceLiveModelFactory.SessionUpdateSessionUpdated());
        await _log.WaitUntilAsync(_ => ResponsesRequested() > 0);
    }

    private int ResponsesRequested() => _log.Entries.Count(e => e.StartsWith("response:", StringComparison.Ordinal));

    private static SessionUpdateResponseCreated ResponseCreated() =>
        VoiceLiveModelFactory.SessionUpdateResponseCreated(response: VoiceLiveModelFactory.SessionResponse(id: "r"));

    private static SessionUpdateResponseDone ResponseDone() => VoiceLiveModelFactory.SessionUpdateResponseDone(
        response: VoiceLiveModelFactory.SessionResponse(id: "r", status: SessionResponseStatus.Completed));

    private static SessionUpdateResponseDone FailedResponseDone() => VoiceLiveModelFactory.SessionUpdateResponseDone(
        response: VoiceLiveModelFactory.SessionResponse(id: "r", status: SessionResponseStatus.Failed));
}
