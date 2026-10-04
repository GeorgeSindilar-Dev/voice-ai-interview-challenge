using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace VoiceReset.Features.Phone;

/// <summary>
/// One-time tokens that tie a media stream to a call announced by a signed webhook: the webhook issues one, and the
/// stream must present it in its start message within 30 seconds. Kept in memory (single instance).
/// </summary>
public sealed class PhoneCallTokens(TimeProvider time)
{
    private static readonly TimeSpan s_lifetime = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _issued = new();

    public string Issue()
    {
        var now = time.GetUtcNow();
        foreach (var (token, expiresAt) in _issued)
        {
            if (expiresAt <= now)
            {
                _issued.TryRemove(token, out _);   // unused tokens of calls that never streamed
            }
        }
        var issued = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _issued[issued] = now + s_lifetime;
        return issued;
    }

    /// <summary>True once per issued token, before it expires.</summary>
    public bool Redeem(string? token) =>
        token is not null && _issued.TryRemove(token, out var expiresAt) && time.GetUtcNow() < expiresAt;
}
