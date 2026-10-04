using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace VoiceReset.Features.Phone.Twilio;

/// <summary>
/// Checks the X-Twilio-Signature header: Base64(HMAC-SHA1(auth token, full URL followed by every POST parameter's
/// name and value, sorted by name)). Only Twilio, which knows the auth token, can produce it.
/// </summary>
public static class TwilioSignature
{
    [SuppressMessage("Security", "CA5350", Justification = "Twilio defines the signature as HMAC-SHA1; we only verify it.")]
    public static string Compute(string authToken, string url, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var data = new StringBuilder(url);
        foreach (var (name, value) in parameters.OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Value, StringComparer.Ordinal))
        {
            data.Append(name).Append(value);
        }
        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString()));
        return Convert.ToBase64String(hash);
    }

    public static bool IsValid(string authToken, string url, IEnumerable<KeyValuePair<string, string>> parameters, string? signature) =>
        !string.IsNullOrEmpty(signature)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Compute(authToken, url, parameters)), Encoding.UTF8.GetBytes(signature));
}
