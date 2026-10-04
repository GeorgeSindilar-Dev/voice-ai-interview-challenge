using VoiceReset.Shared.Storage;

namespace VoiceReset.Features.Recovery;

/// <summary>Call sessions as JSON documents: sessions/&lt;sessionId&gt;.json.</summary>
public sealed class SessionStore(IJsonStore store)
{
    private const string Prefix = "sessions/";

    public Task<CallSession?> GetAsync(string sessionId, CancellationToken ct) => store.ReadAsync<CallSession>(Key(sessionId), ct);

    public Task SaveAsync(CallSession session, CancellationToken ct) => store.WriteAsync(Key(session.SessionId), session, ct);

    public async Task<IReadOnlyList<CallSession>> ListUnsettledAsync(CancellationToken ct)
    {
        List<CallSession> unsettled = [];
        foreach (var key in await store.ListKeysAsync(Prefix, ct))
        {
            if (await store.ReadAsync<CallSession>(key, ct) is { IsUnsettled: true } session)
            {
                unsettled.Add(session);
            }
        }
        return unsettled;
    }

    private static string Key(string sessionId) => $"{Prefix}{sessionId}.json";
}
