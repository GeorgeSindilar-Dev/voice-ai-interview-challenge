using System.Collections.Concurrent;
using System.Text.Json;

namespace VoiceReset.Storage;

/// <summary>
/// Keeps documents as JSON strings, so a read returns a copy, exactly like the blob store.
/// Used by tests and local runs without Azure.
/// </summary>
public sealed class InMemoryJsonStore : IJsonStore
{
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);

    public Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class =>
        Task.FromResult(_documents.TryGetValue(key, out var json)
            ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)
            : null);

    public Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class
    {
        _documents[key] = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. _documents.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)]);
}
