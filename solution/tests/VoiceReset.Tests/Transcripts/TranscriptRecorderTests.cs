using Microsoft.Extensions.Time.Testing;
using VoiceReset.Transcripts;

namespace VoiceReset.Tests.Transcripts;

public sealed class TranscriptRecorderTests
{
    [Fact]
    public void ToDocument_AfterTurns_KeepsOrderAndMasks()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var recorder = new TranscriptRecorder("s1", "browser", time);
        recorder.AddAgent("Hi, I'm an automated assistant.");
        time.Advance(TimeSpan.FromMilliseconds(1500));
        recorder.AddCaller("The code is zero four seven one one two");
        time.Advance(TimeSpan.FromSeconds(2));
        recorder.AddCaller("   ");
        recorder.AddAgent("Thanks.");

        // Act
        var doc = recorder.ToDocument("caller_hangup");

        // Assert
        TranscriptTurn[] expected =
        [
            new("agent", 0, "Hi, I'm an automated assistant."),
            new("caller", 1500, "The code is [CODE]"),
            new("agent", 3500, "Thanks."),
        ];
        Assert.Equal(expected, doc.Turns);
        Assert.Equal(("s1", "browser", "caller_hangup"), (doc.SessionId, doc.Channel, doc.EndReason));
        Assert.Equal(time.GetUtcNow(), doc.EndedAt);
    }
}
