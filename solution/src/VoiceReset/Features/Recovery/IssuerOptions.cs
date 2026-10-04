namespace VoiceReset.Features.Recovery;

public sealed class IssuerOptions
{
    public const string SectionName = "Issuer";

    /// <summary>The issuer's base URL, ending with '/', for example https://host/mock/.</summary>
    public string BaseUrl { get; set; } = "";

    public string ServiceCredential { get; set; } = "";

    public static bool IsValid(IssuerOptions options) =>
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _)
        && options.BaseUrl.EndsWith('/')
        && options.ServiceCredential.Length > 0;
}
