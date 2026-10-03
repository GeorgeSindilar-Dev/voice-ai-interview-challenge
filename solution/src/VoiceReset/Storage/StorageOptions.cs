namespace VoiceReset.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>For example https://account.blob.core.windows.net/. Empty → in-memory store.</summary>
    public string BlobEndpoint { get; set; } = "";

    public static bool IsValid(StorageOptions options) =>
        options.BlobEndpoint.Length == 0 || Uri.TryCreate(options.BlobEndpoint, UriKind.Absolute, out _);
}
