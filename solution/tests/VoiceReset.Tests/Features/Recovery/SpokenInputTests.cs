using VoiceReset.Features.Recovery;

namespace VoiceReset.Tests.Features.Recovery;

public sealed class SpokenInputTests
{
    [Theory]
    [InlineData("Alex dot Morgan", "alex.morgan")]
    [InlineData("alex dot morgan.", "alex.morgan")]
    [InlineData(".alex", null)]
    public void Username_Spoken_NormalizesOrIsUnclear(string spoken, string? expected) =>
        Assert.Equal(expected, SpokenInput.Username(spoken));

    [Theory]
    [InlineData("oh 4 7-1 one two", "047112")]
    [InlineData("4 7", null)]
    public void Code_Spoken_NormalizesOrIsUnclear(string spoken, string? expected) =>
        Assert.Equal(expected, SpokenInput.Code(spoken));
}
