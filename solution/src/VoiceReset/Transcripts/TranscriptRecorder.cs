namespace VoiceReset.Transcripts;

/// <summary>Final caller/agent sentences of one call. Text is masked as it is added, so raw text is never kept.</summary>
public sealed class TranscriptRecorder(string sessionId, string channel, TimeProvider time)
{
    private readonly TimeProvider _time = time;   // stored, not captured (avoids CS9124 with the initializer below)
    private readonly DateTimeOffset _startedAt = time.GetUtcNow();
    private readonly List<TranscriptTurn> _turns = [];
    private readonly Lock _lock = new();   // caller and agent events can arrive on different threads

    public void AddCaller(string text) => Add("caller", text);

    public void AddAgent(string text) => Add("agent", text);

    public TranscriptDocument ToDocument(string endReason)
    {
        lock (_lock)
        {
            return new(sessionId, channel, _startedAt, _time.GetUtcNow(), endReason, [.. _turns]);
        }
    }

    private void Add(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        var masked = TranscriptMasker.Mask(text);
        lock (_lock)
        {
            var offsetMs = (long)(_time.GetUtcNow() - _startedAt).TotalMilliseconds;
            _turns.Add(new TranscriptTurn(role, offsetMs, masked));
        }
    }
}
