using System.Net.WebSockets;
using VoiceReset.Features.Phone.Twilio;
using VoiceReset.Features.Voice;

namespace VoiceReset.Features.Phone;

/// <summary>
/// The phone channel, independent of the carrier. POST /phone/incoming is the number's voice webhook: the provider
/// checks it is genuine and answers with instructions to stream the call to wss://&lt;host&gt;/phone/stream with a
/// one-time token. The stream then runs the same voice session as the browser. The carrier only carries the call;
/// speech, model and tools stay on Azure.
/// </summary>
public static class PhoneEndpoints
{
    public static IServiceCollection AddPhone(this IServiceCollection services)
    {
        services.AddOptions<TwilioOptions>().BindConfiguration(TwilioOptions.SectionName);
        services.AddHttpClient(TwilioProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<ITelephonyProvider, TwilioProvider>();   // the carrier: change only this registration
        services.AddSingleton<PhoneCallTokens>();
        return services;
    }

    public static IEndpointRouteBuilder MapPhone(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/phone/incoming", AnswerAsync);
        endpoints.Map("/phone/stream", StreamAsync);
        return endpoints;
    }

    // The caller's number is never used or logged: caller ID is not proof of anything.
    public static async Task<IResult> AnswerAsync(HttpRequest request, ITelephonyProvider provider, PhoneCallTokens tokens)
    {
        if (!provider.IsConfigured)
        {
            return Results.NotFound();
        }
        if (!await provider.IsGenuineAsync(request))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        return provider.StreamCallTo($"wss://{request.Host}/phone/stream", tokens.Issue());
    }

    public static async Task StreamAsync(HttpContext context, ITelephonyProvider provider, PhoneCallTokens tokens)
    {
        if (!provider.IsConfigured || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var channel = await provider.AcceptStreamAsync(socket, tokens.Redeem, context.RequestAborted);
        if (channel is null)
        {
            // No start message, or no valid token: not a call our webhook answered.
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "not allowed", context.RequestAborted);
            return;
        }
        await VoiceEndpoints.RunCallAsync(channel, context);
    }
}
