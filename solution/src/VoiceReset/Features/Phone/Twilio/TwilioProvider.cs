using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using VoiceReset.Features.Voice;

namespace VoiceReset.Features.Phone.Twilio;

/// <summary>Twilio: signed form webhooks, TwiML answers, and media streams (TwilioAudioChannel).</summary>
public sealed partial class TwilioProvider(
    IOptions<TwilioOptions> options, IHttpClientFactory httpClients, ILogger<TwilioProvider> logger) : ITelephonyProvider
{
    public const string HttpClientName = "twilio";

    private string AuthToken => options.Value.AuthToken;

    public bool IsConfigured => AuthToken.Length > 0;

    /// <summary>
    /// A signed request is checked by its signature. Twilio's trial number sends no signature, so then the call
    /// itself is looked up at Twilio with the account's credentials: only a live call on our account passes.
    /// </summary>
    public async Task<bool> IsGenuineAsync(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            LogNotForm(logger, request.ContentType ?? "none");
            return false;
        }
        var ct = request.HttpContext.RequestAborted;
        var form = await request.ReadFormAsync(ct);
        string? signature = request.Headers["X-Twilio-Signature"];
        if (!string.IsNullOrEmpty(signature))
        {
            var parameters = form.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value ?? "")));
            // Twilio signs the public https URL it called; App Service passes the request on as http internally.
            var url = $"https://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
            var valid = TwilioSignature.IsValid(AuthToken, url, parameters, signature);
            LogChecked(logger, "signature", valid);
            return valid;
        }
        var live = await IsLiveCallAsync(form["AccountSid"].ToString(), form["CallSid"].ToString(), ct);
        LogChecked(logger, "call lookup", live);
        return live;
    }

    public IResult StreamCallTo(string streamUrl, string token)
    {
        var twiml = new XElement("Response",
            new XElement("Connect",
                new XElement("Stream", new XAttribute("url", streamUrl),
                    new XElement("Parameter", new XAttribute("name", "token"), new XAttribute("value", token)))));
        return Results.Content(twiml.ToString(SaveOptions.DisableFormatting), "text/xml");
    }

    public async Task<IAudioChannel?> AcceptStreamAsync(WebSocket socket, Func<string?, bool> redeemToken, CancellationToken ct) =>
        await TwilioAudioChannel.AcceptAsync(socket, redeemToken, ct);

    /// <summary>True when Twilio confirms, with our auth token, that this call is ringing or in progress on that account.</summary>
    private async Task<bool> IsLiveCallAsync(string accountSid, string callSid, CancellationToken ct)
    {
        if (!AccountSidPattern().IsMatch(accountSid) || !CallSidPattern().IsMatch(callSid))
        {
            return false;
        }
        using var client = httpClients.CreateClient(HttpClientName);
        using var lookup = new HttpRequestMessage(HttpMethod.Get, $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Calls/{callSid}.json");
        lookup.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{AuthToken}")));
        try
        {
            using var response = await client.SendAsync(lookup, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;   // unknown call, or the token is not this account's
            }
            var call = await response.Content.ReadFromJsonAsync<CallResource>(ct);
            return call?.Status is "ringing" or "in-progress" or "queued";
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private sealed record CallResource(string? Status);

    [GeneratedRegex("^AC[0-9a-f]{32}$")]
    private static partial Regex AccountSidPattern();

    [GeneratedRegex("^CA[0-9a-f]{32}$")]
    private static partial Regex CallSidPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "TwilioWebhookNotForm ContentType={ContentType}")]
    private static partial void LogNotForm(ILogger logger, string contentType);

    [LoggerMessage(Level = LogLevel.Information, Message = "TwilioWebhookChecked {Method} Genuine={Genuine}")]
    private static partial void LogChecked(ILogger logger, string method, bool genuine);
}
