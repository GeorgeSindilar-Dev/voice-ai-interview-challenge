using System.Text.Json;

namespace VoiceReset.Mock;

public static class MockJson
{
    // Strict: case-sensitive names, unknown and duplicate members rejected, nullable and required members respected.
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Reads the JSON body; null means invalid JSON, a wrong type, or a missing or unknown field.</summary>
    public static async Task<T?> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        try
        {
            return request.HasJsonContentType() ? await request.ReadFromJsonAsync<T>(Options, ct) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
