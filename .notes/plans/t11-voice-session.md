# T11: Voice Session, Browser Audio Channel and `/voice/ws` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One browser call = one Voice Live session: caller audio in, agent audio out, tools through the workflow, barge-in, safe line, time limit, saved transcript.

**Architecture:** `VoiceSession` owns one call and only sees `IVoiceLiveConnection` (thin SDK wrapper) and `IAudioChannel` (browser WebSocket now, phone later). Three loops (caller audio → Voice Live; Voice Live events → handlers under one lock; the time limit) end the call through one `Finish(reason)`. One response at a time: a tool result's answer waits for `response.done`. Fixed lines are spoken word for word with a pre-generated assistant message (raw JSON). `/voice/ws` needs the `Access` policy and checks the Origin.

**Tech Stack:** Azure.AI.VoiceLive 1.2.0, ASP.NET Core WebSockets, `TimeProvider`, xUnit v3, hand-written fakes. "step-07" = `.notes/archive/overnight/plans/step-07-voice-agent.md` (compiled against 1.2.0); three files are copied from it with the exact changes listed. CA1859, CA1848 and CA1873 are warnings (errors in Release): concrete local/private types, `[LoggerMessage]`, no computed arguments to Information logs. Depends on T2 (`TokenCredential`), T6 (`RecoveryWorkflow`, `SessionStore`), T8, T9, T10. Commands run from `solution/`. Dropped from step-07 on purpose: strikes, output monitor, telemetry, silence timers, concurrency limiter.

---

### Task 1: Options, Voice Live boundary, audio channel (no tests requested)

**Files (all `src/VoiceReset/Voice/`):** `VoiceOptions.cs`, `IVoiceLiveConnection.cs`, `VoiceLiveConnection.cs`, `VoiceLiveSettings.cs`, `IAudioChannel.cs`, `BrowserAudioChannel.cs`

- [ ] **Step 1: Options** — `VoiceOptions.cs` (skip a class another task already created with these keys):
```csharp
using System.ComponentModel.DataAnnotations;

namespace VoiceReset.Voice;

public sealed class VoiceLiveOptions
{
    public const string SectionName = "VoiceLive";
    [Required, Url] public string Endpoint { get; set; } = "";   // https base URI of the Foundry resource
    [Required] public string Model { get; set; } = "gpt-4.1-mini";
    [Required] public string Voice { get; set; } = "en-US-Ava:DragonHDLatestNeural";
}

public sealed class LimitsOptions
{
    public const string SectionName = "Limits";
    [Range(30, 3600)] public int MaxCallSeconds { get; set; } = 600;
}
```

- [ ] **Step 2: The boundary (copied from step-07 Task 5 Step 3, namespace `VoiceReset.Voice` everywhere)**
  - `IVoiceLiveConnection.cs`: the interface at lines 1237–1269 unchanged (`ConfigureAsync`, `SendAudioAsync`, `ReadUpdatesAsync`, `SendFunctionOutputAsync`, `StartResponseAsync(string? instructions, …)`, `SayAsync`, `CancelResponseAsync`; `IAsyncDisposable`).
  - `VoiceLiveConnection.cs`: the class at lines 1285–1336 (its `SayAsync` sends `response.create` with `pre_generated_assistant_message` as raw JSON because `ResponseCreateParams` is internal in 1.2.0), plus this method at the top of the class:
```csharp
    /// <summary>Opens the WebSocket. The model is fixed for the whole session.</summary>
    public static async Task<VoiceLiveConnection> OpenAsync(VoiceLiveClient client, string model, CancellationToken ct) =>
        new(await client.StartSessionAsync(model, ct));
```

  - `VoiceLiveSettings.cs`: the class at lines 1364–1416 with exactly these changes: drop `using VoiceReset.Agent.Configuration;`; signature `Build(VoiceLiveOptions settings, string instructions)` and delete the `options.Metadata[...]` line; transcription model `AudioInputTranscriptionOptionsModel.AzureSpeech` (instead of `new AudioInputTranscriptionOptionsModel(settings.TranscriptionModel)`); delete the `Temperature = …` line; `MaxResponseOutputTokens = new MaxResponseOutputTokensOption(300)`; `foreach (var tool in ToolDefinitions.All)`. Result: model/voice from options, PCM16 24 kHz, azure-speech `en-US`, `azure_semantic_vad` with `RemoveFillerWords`, `InterruptResponse`, `AutoTruncate`, `CreateResponse`, deep noise suppression, server echo cancellation, tools, auto tool choice, no parallel tool calls, 300 output tokens.

- [ ] **Step 3: The audio channel** — `IAudioChannel.cs`:
```csharp
namespace VoiceReset.Voice;

/// <summary>One caller connection (browser now, phone later). It only moves audio; behaviour lives in VoiceSession.</summary>
public interface IAudioChannel
{
    string Name { get; }   // "browser" or "phone": passed to RecoveryWorkflow.StartSessionAsync
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken ct);   // PCM16 24 kHz mono; throws when the connection breaks
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct);
    Task StopPlaybackAsync(CancellationToken ct);   // barge-in: drop audio the caller has not heard
    Task SendCaptionAsync(string text, CancellationToken ct);
    Task SendEndedAsync(string reason, CancellationToken ct);
    Task CloseAsync(CancellationToken ct);
}
```

`BrowserAudioChannel.cs`: copy the class from step-07 Task 6 Step 4 (lines 1773–1873: binary frames up to 64 KiB, browser text frames ignored, ends on Close, one `SemaphoreSlim` around every send, `WebSocketException` on send swallowed) with exactly these changes:
1. Namespace `VoiceReset.Voice`; add `using System.Text.Json;`; summary: server text frames are `{"type":"clear"}`, `{"type":"caption","text":…}`, `{"type":"ended","reason":…}`.
2. `public string Kind => "browser";` → `public string Name => "browser";`; every `ValueTask` → `Task` (also the private `SendAsync`).
3. `CloseAsync(string reason, CancellationToken ct)` → `CloseAsync(CancellationToken ct)` with status description `"ended"`.
4. Replace `StopPlaybackAsync` and `SendControlAsync` with:
```csharp
    public Task StopPlaybackAsync(CancellationToken ct) => SendJsonAsync(new { type = "clear" }, ct);

    public Task SendCaptionAsync(string text, CancellationToken ct) => SendJsonAsync(new { type = "caption", text }, ct);

    public Task SendEndedAsync(string reason, CancellationToken ct) => SendJsonAsync(new { type = "ended", reason }, ct);

    private Task SendJsonAsync<T>(T message, CancellationToken ct) =>
        SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)), WebSocketMessageType.Text, ct);
```

- [ ] **Step 4: Build and commit.** `dotnet build VoiceReset.slnx -c Release` → `Build succeeded.`, `0 Warning(s)`.

```bash
git add src/VoiceReset/Voice
git commit -m "feat(voice): add the Voice Live connection and the browser audio channel"
```

### Task 2: VoiceSession (TDD)

**Files:** Create `src/VoiceReset/Voice/VoiceLog.cs`, `VoiceSession.cs`; tests `tests/VoiceReset.Tests/Voice/VoiceSessionFakes.cs`, `VoiceSessionTests.cs`.

- [ ] **Step 1: Write the fakes** — `tests/VoiceReset.Tests/Voice/VoiceSessionFakes.cs`:
```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Azure.AI.VoiceLive;
using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

public sealed class CallLog   // everything the fakes saw, in order; tests wait on it instead of sleeping
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries];

    public Task Add(string entry)
    {
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public async Task WaitUntilAsync(Func<IReadOnlyList<string>, bool> condition)
    {
        for (var i = 0; i < 500 && !condition(Entries); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(Entries), "Timed out. Log: " + string.Join(" | ", Entries));
    }
}

/// <summary>The test plays the Voice Live service.</summary>
public sealed class FakeVoiceLiveConnection(CallLog log) : IVoiceLiveConnection
{
    private readonly Channel<SessionUpdate> _updates = Channel.CreateUnbounded<SessionUpdate>();

    public void Emit(SessionUpdate update) => _updates.Writer.TryWrite(update);

    public async Task SyncAsync()   // waits until every event emitted so far has been fully handled
    {
        var marker = $"sync-{Guid.NewGuid():N}";
        Emit(VoiceLiveModelFactory.SessionUpdateConversationItemTruncated(itemId: marker));
        await log.WaitUntilAsync(entries => entries.Contains($"read:{marker}"));
    }

    public async IAsyncEnumerable<SessionUpdate> ReadUpdatesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in _updates.Reader.ReadAllAsync(ct))
        {
            // Logged when the session asks for the next event, i.e. after it handled the previous one.
            await log.Add(update is SessionUpdateConversationItemTruncated sync ? $"read:{sync.ItemId}" : $"read:{update.GetType().Name}");
            yield return update;
        }
    }

    public Task ConfigureAsync(VoiceLiveSessionOptions options, CancellationToken ct) => log.Add("configure");
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct) => log.Add("audio-in");
    public Task SendFunctionOutputAsync(string callId, string outputJson, CancellationToken ct) => log.Add($"output:{callId}");
    public Task StartResponseAsync(string? instructions, CancellationToken ct) => log.Add($"response:{instructions}");
    public Task SayAsync(string text, CancellationToken ct) => log.Add($"say:{text}");
    public Task CancelResponseAsync(CancellationToken ct) => log.Add("cancel");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>The test plays the caller.</summary>
public sealed class FakeAudioChannel(CallLog log) : IAudioChannel
{
    private readonly Channel<ReadOnlyMemory<byte>> _incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

    public string Name => "browser";
    public void HangUp() => _incoming.Writer.TryComplete();
    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken ct) => _incoming.Reader.ReadAllAsync(ct);
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct) => log.Add("audio-out");
    public Task StopPlaybackAsync(CancellationToken ct) => log.Add("stop-playback");
    public Task SendCaptionAsync(string text, CancellationToken ct) => log.Add($"caption:{text}");
    public Task SendEndedAsync(string reason, CancellationToken ct) => log.Add($"ended:{reason}");
    public Task CloseAsync(CancellationToken ct) => log.Add("close");
}
```

- [ ] **Step 2: Write the failing tests** — `tests/VoiceReset.Tests/Voice/VoiceSessionTests.cs`. Workflow and dispatcher are the real ones from the app's DI container (in-memory store); `submit_code` in `AwaitingUsername` is refused by state without calling the issuer. T8's `NullTranscriptWriter` stands in for storage.

```csharp
using Azure.AI.VoiceLive;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoiceReset.Recovery;
using VoiceReset.Transcripts;
using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

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
        await StartAndGreetAsync();
        _voiceLive.Emit(ResponseCreated());   // the greeting finishes
        _voiceLive.Emit(ResponseDone());

        _voiceLive.Emit(ResponseCreated());   // the model calls a tool inside a response that is still active
        _voiceLive.Emit(VoiceLiveModelFactory.SessionUpdateResponseFunctionCallArgumentsDone(
            callId: "call-1", arguments: """{"code":"123456"}""", name: ToolDefinitions.SubmitCode));
        await _voiceLive.SyncAsync();
        var requestsBeforeDone = ResponsesRequested();
        _voiceLive.Emit(ResponseDone());
        await _voiceLive.SyncAsync();

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
}
```

- [ ] **Step 3: Run to verify they fail.** `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Voice.VoiceSessionTests"` → build FAILS: `The type or namespace name 'VoiceSession' could not be found`.

- [ ] **Step 4: Log messages** — `src/VoiceReset/Voice/VoiceLog.cs` (IDs, reasons, statuses, codes only; never transcript text or tool arguments):
```csharp
namespace VoiceReset.Voice;

internal static partial class VoiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "CallStarted {SessionId}")]
    public static partial void CallStarted(ILogger logger, string sessionId);
    [LoggerMessage(Level = LogLevel.Information, Message = "CallEnded {SessionId} {Reason} {DurationSeconds}")]
    public static partial void CallEnded(ILogger logger, string sessionId, string reason, int durationSeconds);
    [LoggerMessage(Level = LogLevel.Information, Message = "ToolCalled {SessionId} {Tool} {Status}")]
    public static partial void ToolCalled(ILogger logger, string sessionId, string tool, string status);
    [LoggerMessage(Level = LogLevel.Warning, Message = "ResponseNotCompleted {SessionId} {Status}")]
    public static partial void ResponseNotCompleted(ILogger logger, string sessionId, string? status);
    [LoggerMessage(Level = LogLevel.Warning, Message = "VoiceLiveError {SessionId} {Code}")]
    public static partial void VoiceLiveError(ILogger logger, string sessionId, string? code);
    [LoggerMessage(Level = LogLevel.Information, Message = "CallConnectionClosed {SessionId}")]
    public static partial void ConnectionClosed(ILogger logger, Exception exception, string sessionId);
    [LoggerMessage(Level = LogLevel.Error, Message = "CallStepFailed {SessionId}")]
    public static partial void StepFailed(ILogger logger, Exception exception, string sessionId);
}
```

- [ ] **Step 5: Write the session** — `src/VoiceReset/Voice/VoiceSession.cs`:
```csharp
using Azure.AI.VoiceLive;
using VoiceReset.Recovery;
using VoiceReset.Transcripts;

namespace VoiceReset.Voice;

/// <summary>One call. Three loops end it through Finish(); events are handled one at a time under _turnLock.</summary>
public sealed class VoiceSession(
    RecoveryWorkflow workflow, ToolDispatcher tools, IVoiceLiveConnection voiceLive, IAudioChannel channel,
    ITranscriptWriter transcripts, VoiceLiveOptions voiceOptions, LimitsOptions limits, TimeProvider time,
    ILogger<VoiceSession> logger) : IDisposable
{
    public const string CallDroppedReason = "call_dropped";
    public const string TimeLimitReason = "time_limit";
    public const string GreetingInstruction =
        "Greet the caller in one short sentence. Say you are the automated password reset assistant, then ask for their username.";
    public const string SafeLine = "Sorry, I can't help with that. I can help you reset your password.";
    public const string TimeUpGoodbye = "We've reached the time limit for this call. Please call again to continue. Goodbye.";

    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private readonly TaskCompletionSource<string> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _greeted;
    private bool _responseActive;         // requested or being generated: one response at a time
    private bool _modelResponsePending;   // a tool output waits for the next model response
    private bool _saidSafeLine;           // the active response is the safe line: never repeat it in a loop
    private string? _ending;              // set once: the call is saying goodbye
    private string? _goodbye;             // a goodbye waiting for the active response to finish

    public string SessionId { get; private set; } = "";

    public async Task RunAsync(CancellationToken callAborted)
    {
        SessionId = await workflow.StartSessionAsync(channel.Name, callAborted);
        var recorder = new TranscriptRecorder(SessionId, time);
        var startedAt = time.GetUtcNow();
        VoiceLog.CallStarted(logger, SessionId);

        // The audio loop stops last: cancelling a pending WebSocket receive aborts the socket,
        // and the "ended" message must reach the page first.
        using var stopEvents = CancellationTokenSource.CreateLinkedTokenSource(callAborted);
        using var stopAudio = CancellationTokenSource.CreateLinkedTokenSource(callAborted);
        var audio = RunLoopAsync(() => PumpCallerAudioAsync(stopAudio.Token), stopAudio.Token);
        var events = RunLoopAsync(() => HandleEventsAsync(recorder, stopEvents.Token), stopEvents.Token);
        var timeLimit = RunLoopAsync(() => EnforceTimeLimitAsync(stopEvents.Token), stopEvents.Token);

        var reason = await _finished.Task;
        await stopEvents.CancelAsync();
        await Task.WhenAll(events, timeLimit);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));   // the call's token may be cancelled
        await TryAsync(() => workflow.EndCallAsync(SessionId, reason, cleanup.Token));
        await TryAsync(() => channel.SendEndedAsync(reason, cleanup.Token));
        await TryAsync(() => channel.CloseAsync(cleanup.Token));
        await TryAsync(() => transcripts.SaveAsync(recorder.ToDocument(reason), cleanup.Token));
        await stopAudio.CancelAsync();
        await audio;
        var durationSeconds = (int)(time.GetUtcNow() - startedAt).TotalSeconds;
        VoiceLog.CallEnded(logger, SessionId, reason, durationSeconds);
    }

    public void Dispose() => _turnLock.Dispose();

    private async Task PumpCallerAudioAsync(CancellationToken ct)
    {
        await foreach (var frame in channel.ReadAudioAsync(ct))
        {
            await voiceLive.SendAudioAsync(frame, ct);
        }
    }

    private async Task HandleEventsAsync(TranscriptRecorder recorder, CancellationToken ct)
    {
        await voiceLive.ConfigureAsync(VoiceLiveSettings.Build(voiceOptions, SystemPrompt.Text), ct);
        await foreach (var update in voiceLive.ReadUpdatesAsync(ct))
        {
            await WithTurnLockAsync(() => HandleAsync(update, recorder, ct), ct);
        }
    }

    private async Task EnforceTimeLimitAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(limits.MaxCallSeconds), time, ct);
        await WithTurnLockAsync(() => EndWithGoodbyeAsync(TimeLimitReason, TimeUpGoodbye, ct), ct);
        await Task.Delay(TimeSpan.FromSeconds(15), time, ct);   // the goodbye never finished: end anyway
        Finish(TimeLimitReason);
    }

    private async Task HandleAsync(SessionUpdate update, TranscriptRecorder recorder, CancellationToken ct)
    {
        switch (update)
        {
            case SessionUpdateSessionUpdated when !_greeted:   // also follows later updates: greet once
                _greeted = true;
                await RequestModelResponseAsync(GreetingInstruction, ct);
                break;
            case SessionUpdateInputAudioBufferSpeechStarted:   // barge-in: the service cancels; we drop unheard audio
                await channel.StopPlaybackAsync(ct);
                break;
            case SessionUpdateResponseCreated:   // also answers the service starts at the end of a caller turn
                _responseActive = true;
                break;
            case SessionUpdateResponseAudioDelta delta:
                await channel.SendAudioAsync(delta.Delta.ToMemory(), ct);
                break;
            case SessionUpdateResponseAudioTranscriptDone agent:
                recorder.AddAgent(agent.Transcript);
                await channel.SendCaptionAsync(agent.Transcript, ct);
                break;
            case SessionUpdateConversationItemInputAudioTranscriptionCompleted caller:
                recorder.AddCaller(caller.Transcript);
                break;
            case SessionUpdateResponseFunctionCallArgumentsDone call:
                await OnFunctionCallAsync(call, ct);
                break;
            case SessionUpdateResponseDone done:
                await OnResponseDoneAsync(done.Response, ct);
                break;
            case SessionUpdateError error:
                VoiceLog.VoiceLiveError(logger, SessionId, error.Error?.Code);
                break;
        }
    }

    private async Task OnFunctionCallAsync(SessionUpdateResponseFunctionCallArgumentsDone call, CancellationToken ct)
    {
        var result = await tools.DispatchAsync(SessionId, call.Name, call.Arguments, ct);
        var toolName = ToolDefinitions.All.Any(t => t.Name == call.Name) ? call.Name : "unknown";   // never log invented names
        VoiceLog.ToolCalled(logger, SessionId, toolName, result.Status);
        await voiceLive.SendFunctionOutputAsync(call.CallId, ToolDispatcher.ToOutputJson(result), ct);
        if (result.Status == ToolDispatcher.CallEndingStatus)
        {
            await EndWithGoodbyeAsync(ToolDispatcher.AgentEndedReason, result.Say, ct);
            return;
        }
        await RequestModelResponseAsync(instructions: null, ct);   // waits for response.done if one is active
    }

    private async Task OnResponseDoneAsync(SessionResponse response, CancellationToken ct)
    {
        _responseActive = false;
        var wasSafeLine = _saidSafeLine;
        _saidSafeLine = false;
        if (_ending is not null)
        {
            if (_goodbye is { } goodbye)
            {
                _goodbye = null;
                await SpeakAsync(goodbye, ct);
            }
            else
            {
                Finish(_ending);   // the goodbye was spoken
            }
            return;
        }
        if (IsFilteredOrFailed(response) && !wasSafeLine)
        {
            VoiceLog.ResponseNotCompleted(logger, SessionId, response.Status?.ToString());
            _modelResponsePending = false;
            _saidSafeLine = true;
            await SpeakAsync(SafeLine, ct);   // never silence after a filtered or failed answer
            return;
        }
        if (_modelResponsePending)
        {
            _modelResponsePending = false;
            await RequestModelResponseAsync(instructions: null, ct);
        }
    }

    private async Task RequestModelResponseAsync(string? instructions, CancellationToken ct)
    {
        if (_ending is not null)
        {
            return;
        }
        if (_responseActive)
        {
            _modelResponsePending |= instructions is null;   // a tool answer comes later; a greeting is dropped
            return;
        }
        _responseActive = true;   // set before sending, so two requests never overlap
        await voiceLive.StartResponseAsync(instructions, ct);
    }

    private Task SpeakAsync(string line, CancellationToken ct)
    {
        _responseActive = true;
        return voiceLive.SayAsync(line, ct);
    }

    private async Task EndWithGoodbyeAsync(string reason, string goodbye, CancellationToken ct)
    {
        if (_ending is not null)
        {
            return;
        }
        _ending = reason;
        _modelResponsePending = false;
        if (_responseActive)
        {
            _goodbye = goodbye;   // the agent may finish its sentence first
            return;
        }
        await SpeakAsync(goodbye, ct);
    }

    /// <summary>When a loop stops (caller left, Voice Live closed, fault) the call is dropped, unless it already finished.</summary>
    private async Task RunLoopAsync(Func<Task> loop, CancellationToken ct)
    {
        try
        {
            await loop();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The call ended for another reason.
        }
        catch (Exception ex)
        {
            VoiceLog.ConnectionClosed(logger, ex, SessionId);   // a broken socket is a dropped call, not an error
        }
        Finish(CallDroppedReason);
    }

    private async Task WithTurnLockAsync(Func<Task> step, CancellationToken ct)
    {
        await _turnLock.WaitAsync(ct);
        try
        {
            await step();
        }
        finally
        {
            _turnLock.Release();
        }
    }

    private async Task TryAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            VoiceLog.StepFailed(logger, ex, SessionId);
        }
    }

    private void Finish(string reason) => _finished.TrySetResult(reason);   // the first reason wins

    private static bool IsFilteredOrFailed(SessionResponse response) =>
        response.Status == SessionResponseStatus.Failed
        || (response.StatusDetails is ResponseIncompleteDetails details && details.Reason == ResponseIncompleteDetailsReason.ContentFilter);
}
```

- [ ] **Step 6: Run to verify they pass** (same command → PASS, 4 tests), **then commit:**
```bash
git add src/VoiceReset/Voice/VoiceLog.cs src/VoiceReset/Voice/VoiceSession.cs tests/VoiceReset.Tests/Voice/VoiceSessionFakes.cs tests/VoiceReset.Tests/Voice/VoiceSessionTests.cs
git commit -m "feat(voice): add the voice session call loop"
```

### Task 3: `/voice/ws` endpoint and wiring

**Files:** Create `src/VoiceReset/Voice/VoiceEndpoints.cs`; modify `Program.cs`, `appsettings.json`, `appsettings.Development.json`.

- [ ] **Step 1: Write the endpoint** — `src/VoiceReset/Voice/VoiceEndpoints.cs`:
```csharp
using Azure.AI.VoiceLive;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VoiceReset.Access;
using VoiceReset.Recovery;
using VoiceReset.Transcripts;

namespace VoiceReset.Voice;

public static class VoiceEndpoints
{
    public const string UnavailableReason = "unavailable";

    public static IServiceCollection AddVoice(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VoiceLiveOptions>().Bind(configuration.GetSection(VoiceLiveOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<LimitsOptions>().Bind(configuration.GetSection(LimitsOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        // One client for the app on the shared credential (T2). Created on first use: tests never touch Azure.
        services.AddSingleton(sp => new VoiceLiveClient(
            new Uri(sp.GetRequiredService<IOptions<VoiceLiveOptions>>().Value.Endpoint),
            sp.GetRequiredService<TokenCredential>()));
        return services;
    }

    /// <summary>Only our own page may open the socket: browsers send cookies on cross-site WebSocket handshakes.</summary>
    public static WebApplication UseVoiceWebSockets(this WebApplication app)
    {
        var access = app.Services.GetRequiredService<IOptions<AccessOptions>>().Value;
        var options = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) };
        options.AllowedOrigins.Add(access.AllowedOrigin.TrimEnd('/'));
        app.UseWebSockets(options);
        return app;
    }

    public static IEndpointRouteBuilder MapVoice(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/voice/ws", HandleAsync).RequireAuthorization(AccessGate.Policy);   // 401 without the cookie
        return endpoints;
    }

    public static async Task HandleAsync(
        HttpContext context, VoiceLiveClient client, RecoveryWorkflow workflow, ToolDispatcher tools,
        ITranscriptWriter transcripts, IOptions<VoiceLiveOptions> voiceOptions, IOptions<LimitsOptions> limits,
        TimeProvider time, ILogger<VoiceSession> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var channel = new BrowserAudioChannel(socket);
        var ct = context.RequestAborted;
        VoiceLiveConnection connection;
        try
        {
            connection = await VoiceLiveConnection.OpenAsync(client, voiceOptions.Value.Model, ct);
        }
        catch (OperationCanceledException)
        {
            return;   // the caller left while we were connecting
        }
        catch (Exception ex)
        {
            VoiceLog.StepFailed(logger, ex, "none");
            await channel.SendEndedAsync(UnavailableReason, ct);   // the page shows a message, never exception text
            await channel.CloseAsync(ct);
            return;
        }
        await using (connection)
        {
            using var session = new VoiceSession(workflow, tools, connection, channel, transcripts,
                voiceOptions.Value, limits.Value, time, logger);
            await session.RunAsync(ct);
        }
    }
}
```

- [ ] **Step 2: Configuration and wiring.** `appsettings.json` gets top-level `"VoiceLive": { "Model": "gpt-4.1-mini", "Voice": "en-US-Ava:DragonHDLatestNeural" }` and `"Limits": { "MaxCallSeconds": 600 }` (the endpoint is an App Service setting). `appsettings.Development.json` gets `"VoiceLive": { "Endpoint": "https://dev-only-voicelive.invalid/" }` (for a real local call set `VoiceLive__Endpoint` and run `az login`). `Program.cs` (add `using VoiceReset.Voice;`):
```csharp
builder.Services.AddVoice(builder.Configuration);   // services
app.UseVoiceWebSockets();                           // middleware, before T10's UseAuthentication
app.MapVoice();                                     // endpoints
```

- [ ] **Step 3: Verify.** `dotnet build VoiceReset.slnx -c Release` → `0 Warning(s)`; `dotnet test --project tests/VoiceReset.Tests` → all pass. Manual (app running as in T12): `curl.exe -k -i https://localhost:7180/voice/ws` → `HTTP/1.1 401`; after T12 a real call must greet, answer, and stop talking when interrupted.

- [ ] **Step 4: Commit**

```bash
git add src/VoiceReset/Voice/VoiceEndpoints.cs src/VoiceReset/Program.cs src/VoiceReset/appsettings.json src/VoiceReset/appsettings.Development.json
git commit -m "feat(voice): add the voice WebSocket endpoint"
```

## Questions

1. The pre-generated assistant message (raw JSON `response.create`) is unverified against the live service. If the first real call shows it fails, change `SayAsync` to `session.StartResponseAsync("Say exactly this sentence and nothing else: " + text, ct)`. Accept this fallback?
2. Every lost connection (End button, tab closed, network, Voice Live failure) ends as `call_dropped`. Should the End button send `caller_ended`, and a mid-call Voice Live failure be `dependency_unavailable` (escalation)?
3. Tests assume `SessionStore.GetAsync(string, CancellationToken)` returns `CallSession?` with `string? EndReason`, and that `TranscriptRecorder` masks text itself (T8). Adjust if T6/T8 differ.
4. The time limit lets the current answer finish before the goodbye (no `response.cancel`). Captions show the agent's raw words (incl. the code read-back) on the caller's own screen; only the stored transcript is masked. No rate limit or concurrent-call cap on `/voice/ws`: the access code is the only cost guard. All OK?

## Additions to contracts

- `IAudioChannel.Name` (`"browser"`/`"phone"`), passed to `RecoveryWorkflow.StartSessionAsync`; channel methods return `Task`; `CloseAsync(CancellationToken)`.
- End reasons for `EndCallAsync`: `VoiceSession.CallDroppedReason = "call_dropped"`, `VoiceSession.TimeLimitReason = "time_limit"`, T9's `"agent_ended"`. The browser `ended` message carries these, or `VoiceEndpoints.UnavailableReason = "unavailable"` (Voice Live unreachable; no session started).
- Fixed lines are consts on `VoiceSession` (`GreetingInstruction`, `SafeLine`, `TimeUpGoodbye`). `VoiceLiveOptions` + `LimitsOptions` live in `Voice/VoiceOptions.cs`. `AddVoice(IServiceCollection, IConfiguration)` (also `TryAddSingleton(TimeProvider.System)` and the `VoiceLiveClient` singleton), `UseVoiceWebSockets()`, `MapVoice()`, `VoiceLiveConnection.OpenAsync(client, model, ct)`, `VoiceLiveSettings.Build(options, instructions)`.
- Dev value `VoiceLive:Endpoint = https://dev-only-voicelive.invalid/`; App Service setting `VoiceLive__Endpoint`.
