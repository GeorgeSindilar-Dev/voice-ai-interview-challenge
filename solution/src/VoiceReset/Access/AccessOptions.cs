namespace VoiceReset.Access;

/// <summary>Configuration section "Access". The code comes from App Service settings in Azure.</summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    /// <summary>The shared access code for the agent page.</summary>
    public string Code { get; set; } = "";

    /// <summary>The page origin, for example https://host. The voice socket accepts only this origin.</summary>
    public string AllowedOrigin { get; set; } = "";

    public static bool IsValid(AccessOptions options) =>
        options.Code.Length >= 12
        && Uri.TryCreate(options.AllowedOrigin, UriKind.Absolute, out var origin)
        && origin.AbsolutePath == "/"
        && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp);
}
