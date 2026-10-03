using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace VoiceReset.Storage;

public static class StorageServiceCollectionExtensions
{
    public const string ContainerName = "state";

    /// <summary>
    /// Registers IJsonStore (Blob Storage when Storage:BlobEndpoint is set, otherwise in memory)
    /// and the shared TokenCredential (managed identity in Azure, az login locally).
    /// </summary>
    public static IServiceCollection AddJsonStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(StorageOptions.IsValid, "Storage:BlobEndpoint must be empty or an absolute URL.")
            .ValidateOnStart();

        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton<IJsonStore>(CreateStore);
        return services;
    }

    private static IJsonStore CreateStore(IServiceProvider services)
    {
        var endpoint = services.GetRequiredService<IOptions<StorageOptions>>().Value.BlobEndpoint;
        if (endpoint.Length == 0)
        {
            return new InMemoryJsonStore();
        }

        var container = new BlobServiceClient(new Uri(endpoint), services.GetRequiredService<TokenCredential>())
            .GetBlobContainerClient(ContainerName);
        container.CreateIfNotExists();
        return new BlobJsonStore(container);
    }
}
