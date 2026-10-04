using System.Text.Json;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure;

namespace VoiceReset.Shared.Storage;

/// <summary>One blob per key in the "state" container.</summary>
public sealed class BlobJsonStore(BlobContainerClient container) : IJsonStore
{
    private static readonly BlobUploadOptions s_uploadOptions = new()
    {
        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
    };

    public async Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            var download = await container.GetBlobClient(key).DownloadContentAsync(ct);
            return download.Value.Content.ToObjectFromJson<T>(JsonSerializerOptions.Web);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    public async Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class =>
        await container.GetBlobClient(key)
            .UploadAsync(BinaryData.FromObjectAsJson(value, JsonSerializerOptions.Web), s_uploadOptions, ct);

    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct)
    {
        List<string> keys = [];
        await foreach (var blob in container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, ct))
        {
            keys.Add(blob.Name);
        }

        return keys;
    }
}
