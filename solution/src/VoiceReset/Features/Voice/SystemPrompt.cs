using System.Text;

namespace VoiceReset.Features.Voice;

/// <summary>
/// The system prompt (Features/Voice/system-prompt.md, embedded in the assembly). It holds no secrets.
/// It helps, but the real guardrails are in code: the workflow refuses tools in the wrong state.
/// </summary>
public static class SystemPrompt
{
    private const string ResourceName = "VoiceReset.Features.Voice.system-prompt.md";

    public static string Text { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(SystemPrompt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
