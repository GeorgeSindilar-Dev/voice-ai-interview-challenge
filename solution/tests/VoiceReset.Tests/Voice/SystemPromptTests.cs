using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

public sealed class SystemPromptTests
{
    [Fact]
    public void Text_EmbeddedResource_Loads() => Assert.StartsWith("# Role", SystemPrompt.Text);
}
