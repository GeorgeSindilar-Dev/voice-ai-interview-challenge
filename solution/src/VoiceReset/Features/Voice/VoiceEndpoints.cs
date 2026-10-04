using Azure.AI.VoiceLive;
using Azure.Core;
using Microsoft.Extensions.Options;
using VoiceReset.Features.Access;
using VoiceReset.Features.Recovery;
using VoiceReset.Features.Transcripts;

namespace VoiceReset.Features.Voice;

/// <summary>
/// The browser voice call: GET /voice/ws (WebSocket), access cookie required, own origin only.
/// RunCallAsync is shared with the phone channel.
/// </summary>
public static class VoiceEndpoints
{
    /// <summary>Registers VoiceLiveOptions and one VoiceLiveClient. LimitsOptions come from AddRecovery, the credential from AddJsonStore.</summary>
    public static IServiceCollection AddVoice(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VoiceLiveOptions>()
            .Bind(configuration.GetSection(VoiceLiveOptions.SectionName))
            .Validate(VoiceLiveOptions.IsValid, "VoiceLive:Endpoint must be an https URL, and VoiceLive:Model and VoiceLive:Voice are required.")
            .ValidateOnStart();
        // One client for the app on the shared credential. Created on first use, so tests never touch Azure.
        services.AddSingleton(sp => new VoiceLiveClient(
            new Uri(sp.GetRequiredService<IOptions<VoiceLiveOptions>>().Value.Endpoint),
            sp.GetRequiredService<TokenCredential>()));
        return services;
    }

    /// <summary>Only our own page may open the socket: browsers send cookies on cross-site WebSocket handshakes.</summary>
    public static WebApplication UseVoiceWebSockets(this WebApplication app)
    {
        var access = app.Services.GetRequiredService<IOptions<AccessOptions>>().Value;
        var options = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) };
        // The browser's Origin header has no trailing slash or path.
        options.AllowedOrigins.Add(new Uri(access.AllowedOrigin).GetLeftPart(UriPartial.Authority));
        app.UseWebSockets(options);
        return app;
    }

    public static IEndpointRouteBuilder MapVoice(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/voice/ws", HandleAsync).RequireAuthorization(AccessGate.Policy);   // 401 without the cookie
        return endpoints;
    }

    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var channel = new BrowserAudioChannel(socket);
        await RunCallAsync(channel, context);
    }

    /// <summary>One call on any channel (browser or phone): connects to Voice Live and runs the session until it ends.</summary>
    public static async Task RunCallAsync(IAudioChannel channel, HttpContext context)
    {
        var services = context.RequestServices;
        var voiceOptions = services.GetRequiredService<IOptions<VoiceLiveOptions>>();
        var logger = services.GetRequiredService<ILogger<VoiceSession>>();
        var ct = context.RequestAborted;
        VoiceLiveConnection connection;
        try
        {
            connection = await VoiceLiveConnection.OpenAsync(services.GetRequiredService<VoiceLiveClient>(), voiceOptions.Value.Model, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;   // the caller left while we were connecting
        }
        catch (Exception ex)   // includes connect and token timeouts
        {
            var exceptionType = ex.GetType().Name;
            VoiceLog.StepFailed(logger, "none", exceptionType);
            await VoiceSession.EndUnavailableAsync(channel);
            return;
        }
        await using (connection)
        {
            using var session = new VoiceSession(
                services.GetRequiredService<RecoveryWorkflow>(), services.GetRequiredService<ToolDispatcher>(), connection, channel,
                services.GetRequiredService<ITranscriptWriter>(), voiceOptions.Value,
                services.GetRequiredService<IOptions<LimitsOptions>>().Value, services.GetRequiredService<TimeProvider>(), logger);
            await session.RunAsync(ct);
        }
    }
}
