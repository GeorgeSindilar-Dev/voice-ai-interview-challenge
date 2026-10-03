using VoiceReset.Storage;

namespace VoiceReset.Recovery;

/// <summary>Call sessions as JSON documents: sessions/&lt;sessionId&gt;.json.</summary>
public sealed class SessionStore(IJsonStore store)
{
    private const string Prefix = "sessions/";

    public Task<CallSession?> GetAsync(string sessionId, CancellationToken ct) => store.ReadAsync<CallSession>(Key(sessionId), ct);

    public Task SaveAsync(CallSession session, CancellationToken ct) => store.WriteAsync(Key(session.SessionId), session, ct);

    public async Task<IReadOnlyList<CallSession>> ListOpenAsync(CancellationToken ct)
    {
        List<CallSession> open = [];
        foreach (var key in await store.ListKeysAsync(Prefix, ct))
        {
            if (await store.ReadAsync<CallSession>(key, ct) is { IsOpen: true } session)
            {
                open.Add(session);
            }
        }
        return open;
    }

    private static string Key(string sessionId) => $"{Prefix}{sessionId}.json";
}
