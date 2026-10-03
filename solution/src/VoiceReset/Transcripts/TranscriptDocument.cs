namespace VoiceReset.Transcripts;

/// <summary>Role is "caller" or "agent". Text is already masked.</summary>
public sealed record TranscriptTurn(string Role, long OffsetMs, string Text);

/// <summary>One call's masked transcript, stored as JSON with System.Text.Json web defaults (camelCase).</summary>
public sealed record TranscriptDocument(
    string SessionId,
    string Channel,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string EndReason,
    IReadOnlyList<TranscriptTurn> Turns);
