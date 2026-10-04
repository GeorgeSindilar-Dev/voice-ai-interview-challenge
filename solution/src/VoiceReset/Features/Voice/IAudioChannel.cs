namespace VoiceReset.Features.Voice;

/// <summary>The audio a channel carries in both directions; Voice Live is told which one per call.</summary>
public enum ChannelAudio
{
    /// <summary>The browser page: PCM16, 24 kHz, mono.</summary>
    Pcm16At24kHz,

    /// <summary>The phone: G.711 μ-law, 8 kHz, mono (what the carrier streams).</summary>
    MuLawAt8kHz,
}

/// <summary>One caller connection (browser or phone). It only moves audio; behaviour lives in VoiceSession.</summary>
public interface IAudioChannel : IDisposable
{
    /// <summary>"browser" or "phone": passed to RecoveryWorkflow.StartSessionAsync.</summary>
    string Name { get; }

    ChannelAudio Audio { get; }

    /// <summary>Caller audio in the channel's format. Ends when the caller hangs up; throws when the connection breaks.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken ct);

    Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken ct);

    /// <summary>Barge-in: drop audio the caller has not heard yet.</summary>
    Task StopPlaybackAsync(CancellationToken ct);

    /// <summary>One line of the conversation; speaker is "agent" or "caller". Channels without a screen ignore it.</summary>
    Task SendCaptionAsync(string speaker, string text, CancellationToken ct);

    Task SendEndedAsync(string reason, CancellationToken ct);

    Task CloseAsync(CancellationToken ct);
}
