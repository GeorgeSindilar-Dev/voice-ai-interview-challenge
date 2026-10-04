using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;
using Azure;

namespace VoiceReset.Features.Transcripts;

public interface ITranscriptWriter
{
    Task SaveAsync(TranscriptDocument doc, CancellationToken ct);
}

/// <summary>Local runs and tests without Blob Storage: transcripts are not kept.</summary>
public sealed class NullTranscriptWriter : ITranscriptWriter
{
    public Task SaveAsync(TranscriptDocument doc, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// One blob per call: transcripts/yyyy/MM/dd/&lt;sessionId&gt;.json (UTC start date). Create-only: an existing blob is
/// never overwritten (Azure returns 409; the caller logs and continues).
/// </summary>
public sealed class BlobTranscriptWriter(BlobContainerClient container) : ITranscriptWriter
{
    public async Task SaveAsync(TranscriptDocument doc, CancellationToken ct)
    {
        var name = $"{doc.StartedAt.UtcDateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}/{doc.SessionId}.json";
        var content = BinaryData.FromObjectAsJson(doc, JsonSerializerOptions.Web);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },   // create-only
        };
        await container.GetBlobClient(name).UploadAsync(content, options, ct);
    }
}
