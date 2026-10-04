namespace VoiceReset.Features.Voice;

/// <summary>Configuration section "VoiceLive". The endpoint comes from App Service settings in Azure.</summary>
public sealed class VoiceLiveOptions
{
    public const string SectionName = "VoiceLive";

    /// <summary>The https base URI of the Foundry resource, for example https://name.services.ai.azure.com/.</summary>
    public string Endpoint { get; set; } = "";

    public string Model { get; set; } = "gpt-4.1-mini";

    public string Voice { get; set; } = "en-US-Ava:DragonHDLatestNeural";

    public static bool IsValid(VoiceLiveOptions options) =>
        Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == Uri.UriSchemeHttps
        && options.Model.Length > 0
        && options.Voice.Length > 0;
}
