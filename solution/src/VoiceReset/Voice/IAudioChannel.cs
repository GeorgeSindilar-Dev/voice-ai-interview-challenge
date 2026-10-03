namespace VoiceReset.Voice;

/// <summary>One caller connection (browser now, phone later). It only moves audio; behaviour lives in VoiceSession.</summary>
public interface IAudioChannel
{
    /// <summary>"browser" or "phone": passed to RecoveryWorkflow.StartSessionAsync.</summary>
    string Name { get; }

    /// <summary>PCM16 24 kHz mono from the caller. Ends when the caller hangs up; throws when the connection breaks.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken ct);

    Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct);

    /// <summary>Barge-in: drop audio the caller has not heard yet.</summary>
    Task StopPlaybackAsync(CancellationToken ct);

    Task SendCaptionAsync(string text, CancellationToken ct);

    Task SendEndedAsync(string reason, CancellationToken ct);

    Task CloseAsync(CancellationToken ct);
}
