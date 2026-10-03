using Azure.AI.VoiceLive;
using VoiceReset.Recovery;
using VoiceReset.Transcripts;

namespace VoiceReset.Voice;

/// <summary>
/// One call: caller audio to Voice Live, Voice Live events (agent audio, captions, tools) back to the caller.
/// Three loops (caller audio, Voice Live events, the time limit) end the call through Finish(); events are
/// handled one at a time under _turnLock. Only one response is active at a time.
/// </summary>
public sealed class VoiceSession(
    RecoveryWorkflow workflow, ToolDispatcher tools, IVoiceLiveConnection voiceLive, IAudioChannel channel,
    ITranscriptWriter transcripts, VoiceLiveOptions voiceOptions, LimitsOptions limits, TimeProvider time,
    ILogger<VoiceSession> logger) : IDisposable
{
    public const string CallDroppedReason = "call_dropped";
    public const string TimeLimitReason = "time_limit";
    /// <summary>The "ended" reason when no call could start (store or Voice Live unreachable).</summary>
    public const string UnavailableReason = "unavailable";
    public const string GreetingInstruction =
        "Greet the caller in one short sentence. Say you are the automated password reset assistant, then ask for their username.";
    public const string SafeLine = "Sorry, I can't help with that. I can help you reset your password.";
    public const string TimeUpGoodbye = "We've reached the time limit for this call. Please call again to continue. Goodbye.";

    private static readonly TimeSpan s_goodbyeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan s_cleanupTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private readonly TaskCompletionSource<string> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _endingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _greeted;
    private bool _responseActive;         // requested or being generated: one response at a time
    private bool _modelResponsePending;   // a tool output waits for the next model response
    private bool _saidSafeLine;           // the active response is the safe line: never repeat it in a loop
    private string? _ending;              // set once: the call is saying goodbye
    private string? _goodbye;             // a goodbye waiting for the active response to finish

    public string SessionId { get; private set; } = "";

    public async Task RunAsync(CancellationToken callAborted)
    {
        try
        {
            SessionId = await workflow.StartSessionAsync(channel.Name, callAborted);
        }
        catch (Exception ex) when (!callAborted.IsCancellationRequested)
        {
            var exceptionType = ex.GetType().Name;
            VoiceLog.StepFailed(logger, "none", exceptionType);
            await TryAsync(() => EndUnavailableAsync(channel));
            return;
        }
        var recorder = new TranscriptRecorder(SessionId, channel.Name, time);
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
        reason = _ending ?? reason;   // a hang-up during the goodbye still ends as agent_ended or time_limit
        using var cleanup = new CancellationTokenSource(s_cleanupTimeout);   // the call's token may be cancelled already
        await TryAsync(() => workflow.EndCallAsync(SessionId, reason, cleanup.Token));
        await TryAsync(() => channel.SendEndedAsync(reason, cleanup.Token));
        await TryAsync(() => channel.CloseAsync(cleanup.Token));
        await TryAsync(() => transcripts.SaveAsync(recorder.ToDocument(reason), cleanup.Token));   // once, at the end
        await stopAudio.CancelAsync();
        await audio;
        var durationSeconds = (int)(time.GetUtcNow() - startedAt).TotalSeconds;
        VoiceLog.CallEnded(logger, SessionId, reason, durationSeconds);
    }

    /// <summary>No call could start: the page gets "ended: unavailable" (never exception text), then the socket closes.</summary>
    public static async Task EndUnavailableAsync(IAudioChannel channel)
    {
        using var cleanup = new CancellationTokenSource(s_cleanupTimeout);   // the request's token may be cancelled already
        await channel.SendEndedAsync(UnavailableReason, cleanup.Token);
        await channel.CloseAsync(cleanup.Token);
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
        // Rule: once a goodbye starts (end_call or time limit), the call ends within the goodbye timeout, confirmed or not.
        await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(limits.MaxCallSeconds), time, ct), _endingStarted.Task);
        await WithTurnLockAsync(() => EndWithGoodbyeAsync(TimeLimitReason, TimeUpGoodbye, ct), ct);   // no-op if one started
        await Task.Delay(s_goodbyeTimeout, time, ct);
        Finish(_ending ?? TimeLimitReason);
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
            case SessionUpdateResponseCreated:   // also the answers the service starts at the end of a caller turn
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
        if (_ending is not null)
        {
            return;   // saying goodbye: no new actions
        }
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
        _endingStarted.TrySetResult();   // starts the goodbye timeout
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
            var exceptionType = ex.GetType().Name;
            VoiceLog.ConnectionClosed(logger, SessionId, exceptionType);   // a broken socket is a dropped call, not an error
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

    /// <summary>Cleanup steps: one failing (e.g. a 409 for an existing transcript) must not stop the others.</summary>
    private async Task TryAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            VoiceLog.StepFailed(logger, SessionId, ex.GetType().Name);
        }
    }

    private void Finish(string reason) => _finished.TrySetResult(reason);   // the first reason wins

    private static bool IsFilteredOrFailed(SessionResponse response) =>
        response.Status == SessionResponseStatus.Failed
        || (response.StatusDetails is ResponseIncompleteDetails details && details.Reason == ResponseIncompleteDetailsReason.ContentFilter);
}
