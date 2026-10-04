using VoiceReset.Features.Voice;

namespace VoiceReset.Tests.Features.Voice;

public sealed class SystemPromptTests
{
    [Fact]
    public void Text_EmbeddedResource_Loads() => Assert.StartsWith("# Role", SystemPrompt.Text);
}
