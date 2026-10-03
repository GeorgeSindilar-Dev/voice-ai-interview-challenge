using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using VoiceReset.Storage;

namespace VoiceReset.Transcripts;

public static class TranscriptServiceCollectionExtensions
{
    public const string ContainerName = "transcripts";

    /// <summary>
    /// Registers ITranscriptWriter: the blob writer when Storage:BlobEndpoint is set (shared TokenCredential), else the null
    /// writer. Call after AddJsonStore, which registers StorageOptions and the TokenCredential.
    /// </summary>
    public static IServiceCollection AddTranscripts(this IServiceCollection services) =>
        services.AddSingleton<ITranscriptWriter>(CreateWriter);

    private static ITranscriptWriter CreateWriter(IServiceProvider services)
    {
        var endpoint = services.GetRequiredService<IOptions<StorageOptions>>().Value.BlobEndpoint;
        if (endpoint.Length == 0)
        {
            return new NullTranscriptWriter();
        }

        var container = new BlobServiceClient(new Uri(endpoint), services.GetRequiredService<TokenCredential>())
            .GetBlobContainerClient(ContainerName);
        container.CreateIfNotExists();
        return new BlobTranscriptWriter(container);
    }
}
