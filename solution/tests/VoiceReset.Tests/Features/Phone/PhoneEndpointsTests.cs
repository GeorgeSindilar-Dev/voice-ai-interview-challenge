using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Features.Phone;
using VoiceReset.Features.Phone.Twilio;

namespace VoiceReset.Tests.Features.Phone;

public sealed class PhoneEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AuthToken = "test-auth-token";
    private const string WebhookUrl = "https://localhost/phone/incoming";   // Twilio signs the public https URL
    private static readonly Dictionary<string, string> s_call = new() { ["CallSid"] = "CA123", ["From"] = "+15550100", ["To"] = "+18005550100" };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> PhoneApp => factory.WithWebHostBuilder(b => b.UseSetting("Phone:Twilio:AuthToken", AuthToken));

    [Fact]
    public void Signature_TwilioDocumentedExample_Matches()
    {
        // The example from Twilio's webhook security documentation.
        var parameters = new Dictionary<string, string>
        {
            ["CallSid"] = "CA1234567890ABCDE", ["Caller"] = "+12349013030", ["Digits"] = "1234",
            ["From"] = "+12349013030", ["To"] = "+18005551212",
        };

        var signature = TwilioSignature.Compute("12345", "https://mycompany.com/myapp.php?foo=1&bar=2", parameters);

        Assert.Equal("0/KCTR6DLpKmkAf8muzZqo1nDgQ=", signature);
    }

    [Fact]
    public async Task Answer_ValidSignature_StreamsTheCallWithAOneTimeToken()
    {
        using var client = PhoneApp.CreateClient();

        using var response = await PostWebhookAsync(client, TwilioSignature.Compute(AuthToken, WebhookUrl, s_call));
        var twiml = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<Stream url=\"wss://localhost/phone/stream\"><Parameter name=\"token\" value=\"", twiml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_WrongSignature_IsRefused()
    {
        using var client = PhoneApp.CreateClient();

        using var response = await PostWebhookAsync(client, TwilioSignature.Compute("another-token", WebhookUrl, s_call));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Answer_UnsignedWithoutALiveCall_IsRefused()
    {
        // No signature (like the trial number): only a call Twilio confirms passes; "CA123" is not even a valid call SID.
        using var client = PhoneApp.CreateClient();

        using var response = await PostWebhookAsync(client, signature: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Answer_PhoneNotConfigured_IsNotFound()
    {
        using var client = factory.WithWebHostBuilder(b => b.UseSetting("Phone:Twilio:AuthToken", "")).CreateClient();

        using var response = await PostWebhookAsync(client, "anything");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stream_WithoutAValidToken_IsClosedBeforeAnySession()
    {
        var server = PhoneApp.Server;
        var socket = await server.CreateWebSocketClient().ConnectAsync(new Uri(server.BaseAddress, "phone/stream"), Ct);

        await SendAsync(socket, """{"event":"connected","protocol":"Call","version":"1.0.0"}""");
        await SendAsync(socket, """{"event":"start","streamSid":"MZ1","start":{"streamSid":"MZ1","callSid":"CA1","customParameters":{"token":"made-up"}}}""");
        var result = await socket.ReceiveAsync(new byte[256], Ct);

        Assert.Equal((WebSocketMessageType.Close, WebSocketCloseStatus.PolicyViolation), (result.MessageType, result.CloseStatus));
    }

    [Fact]
    public void Tokens_AreOneTimeAndExpire()
    {
        var time = new FakeTimeProvider();
        var tokens = new PhoneCallTokens(time);
        var used = tokens.Issue();
        var late = tokens.Issue();

        var first = tokens.Redeem(used);
        var second = tokens.Redeem(used);
        time.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal((true, false, false), (first, second, tokens.Redeem(late)));
    }

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/phone/incoming") { Content = new FormUrlEncodedContent(s_call) };
        if (signature is not null)
        {
            request.Headers.Add("X-Twilio-Signature", signature);
        }
        return client.SendAsync(request, Ct);
    }

    private static Task SendAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, Ct);
}
