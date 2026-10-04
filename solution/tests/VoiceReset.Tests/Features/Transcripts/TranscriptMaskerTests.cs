using VoiceReset.Features.Transcripts;

namespace VoiceReset.Tests.Features.Transcripts;

public sealed class TranscriptMaskerTests
{
    [Theory]
    [InlineData("my code is 0 4 7 1 1 2", "my code is [CODE]")]
    [InlineData("it's 047112.", "it's [CODE].")]
    [InlineData("zero four seven one one two", "[CODE]")]
    [InlineData("Oh four seven, double one two", "[CODE]")]
    [InlineData("my password is Summer2026!", "my password is [REDACTED]")]
    [InlineData("my password's Summer2026!", "my password's [REDACTED]")]
    [InlineData("password: hunter 2", "password: [REDACTED]")]
    [InlineData("forty-seven, eleven, twenty", "[CODE]")]
    [InlineData("the new pass word would be Blue-Sky-Rocket", "the new pass word would be [REDACTED]")]
    [InlineData("open https://app.example/reset/#token=abc123 now", "open [LINK] now")]
    [InlineData("I have 2 laptops", "I have 2 laptops")]
    [InlineData("Hello, I need to reset my password.", "Hello, I need to reset my password.")]
    [InlineData("one moment, oh I see", "one moment, oh I see")]
    public void Mask_Text_HidesCodesPasswordsAndLinks(string text, string expected)
    {
        // Act
        var masked = TranscriptMasker.Mask(text);

        // Assert
        Assert.Equal(expected, masked);
    }
}
