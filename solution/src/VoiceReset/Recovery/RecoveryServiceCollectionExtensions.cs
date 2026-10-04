using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VoiceReset.Voice;

namespace VoiceReset.Recovery;

public static class RecoveryServiceCollectionExtensions
{
    private static readonly TimeSpan s_issuerTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_connectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Registers the issuer client, the session store, the recovery workflow, the call limits and the open session check.</summary>
    public static IServiceCollection AddRecovery(this IServiceCollection services)
    {
        services.AddOptions<IssuerOptions>()
            .BindConfiguration(IssuerOptions.SectionName)
            .Validate(IssuerOptions.IsValid, "Issuer:BaseUrl must be an absolute URL ending with '/', and Issuer:ServiceCredential is required.")
            .ValidateOnStart();
        services.AddOptions<LimitsOptions>()
            .BindConfiguration(LimitsOptions.SectionName)
            .Validate(LimitsOptions.IsValid, "Limits:MaxCallSeconds must be between 60 and 3600.")
            .ValidateOnStart();

        // The singleton workflow keeps one client, so the handler recycles its connections itself.
        services.AddHttpClient<IssuerClient>(ConfigureIssuer)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = s_connectionLifetime })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SessionStore>();
        services.AddSingleton<RecoveryWorkflow>();
        services.AddHostedService<OpenSessionCheck>();
        return services;
    }

    private static void ConfigureIssuer(IServiceProvider services, HttpClient http)
    {
        var options = services.GetRequiredService<IOptions<IssuerOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ServiceCredential);
        http.Timeout = s_issuerTimeout;
    }
}
