using Azure.AI.VoiceLive;

namespace VoiceReset.Voice;

/// <summary>
/// One open Voice Live session, reduced to what VoiceSession needs. It exists so VoiceSession
/// can be tested with a fake (an I/O boundary). Disposing it closes the session.
/// </summary>
public interface IVoiceLiveConnection : IAsyncDisposable
{
    /// <summary>session.update: prompt, voice, tools, turn detection, audio settings.</summary>
    Task ConfigureAsync(VoiceLiveSessionOptions options, CancellationToken ct);

    /// <summary>input_audio_buffer.append with PCM16 24 kHz mono. The service's VAD commits turns.</summary>
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct);

    /// <summary>All server events. Only one reader per session.</summary>
    IAsyncEnumerable<SessionUpdate> ReadUpdatesAsync(CancellationToken ct);

    /// <summary>conversation.item.create with a function_call_output item.</summary>
    Task SendFunctionOutputAsync(string callId, string outputJson, CancellationToken ct);

    /// <summary>response.create; the model writes the answer, optionally with extra instructions for this response only.</summary>
    Task StartResponseAsync(string? instructions, CancellationToken ct);

    /// <summary>response.create with a pre-generated assistant message: the voice speaks exactly this text, no model involved.</summary>
    Task SayAsync(string text, CancellationToken ct);
}
