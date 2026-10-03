using Azure.Monitor.OpenTelemetry.AspNetCore;

namespace VoiceReset.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Exports traces, metrics and logs to Application Insights when
    /// APPLICATIONINSIGHTS_CONNECTION_STRING is set (App Service app setting).
    /// Locally and in tests it is not set, so nothing is exported.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            services.AddOpenTelemetry().UseAzureMonitor();
        }

        return services;
    }
}
