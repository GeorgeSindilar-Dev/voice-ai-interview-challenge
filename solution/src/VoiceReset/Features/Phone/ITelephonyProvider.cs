using System.Net.WebSockets;
using VoiceReset.Features.Voice;

namespace VoiceReset.Features.Phone;

/// <summary>
/// A phone carrier (Twilio today). The phone endpoints and the voice session only use this interface:
/// another carrier is a new implementation and a different registration in AddPhone.
/// </summary>
public interface ITelephonyProvider
{
    /// <summary>False until the carrier's credentials are configured; the phone routes then answer 404.</summary>
    bool IsConfigured { get; }

    /// <summary>True when the incoming-call webhook request really comes from the carrier (its signature).</summary>
    Task<bool> IsGenuineAsync(HttpRequest request);

    /// <summary>The webhook answer: the carrier's instructions to stream this call to streamUrl, carrying the token.</summary>
    IResult StreamCallTo(string streamUrl, string token);

    /// <summary>
    /// Reads the stream's opening messages and returns the call's audio channel, or null unless the stream
    /// carries a token that redeemToken accepts (so only calls our webhook answered get a voice session).
    /// </summary>
    Task<IAudioChannel?> AcceptStreamAsync(WebSocket socket, Func<string?, bool> redeemToken, CancellationToken ct);
}
