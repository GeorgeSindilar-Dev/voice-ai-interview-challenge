namespace VoiceReset.Storage;

/// <summary>Stores JSON documents by key (for example "mock/state.json" or "sessions/{id}.json").</summary>
public interface IJsonStore
{
    /// <summary>Returns the document, or null when the key does not exist.</summary>
    Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class;

    /// <summary>Creates or replaces the document.</summary>
    Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class;

    /// <summary>Returns the keys that start with the prefix, in ordinal order.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct);
}
