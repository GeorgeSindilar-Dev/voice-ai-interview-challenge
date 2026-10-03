using Microsoft.Extensions.Options;
using VoiceReset.Voice;

namespace VoiceReset.Recovery;

/// <summary>
/// Once per process start: settles sessions a previous process left open.
/// Runs after ApplicationStarted because the issuer is called over HTTP on this same app; never fails startup.
/// </summary>
public sealed partial class StartupCheck(
    SessionStore sessions, IServiceProvider services, IOptions<LimitsOptions> limits,
    IHostApplicationLifetime lifetime, ILogger<StartupCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WhenStartedAsync(stoppingToken);
            var workflow = services.GetRequiredService<RecoveryWorkflow>(); // resolved after start: the test server's handler can only be created once it is running
            var open = await sessions.ListOpenAsync(stoppingToken);
            var maxCallAge = TimeSpan.FromSeconds(limits.Value.MaxCallSeconds);
            var settled = 0;
            foreach (var session in open)
            {
                try
                {
                    if (await workflow.ReconcileAsync(session.SessionId, maxCallAge, stoppingToken))
                    {
                        settled++;
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    LogSessionFailed(logger, session.SessionId, ex.GetType().Name);
                }
            }
            LogDone(logger, open.Count, settled);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailed(logger, ex.GetType().Name); // e.g. storage unreachable: the app still starts
        }
    }

    private async Task WhenStartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Startup check: {OpenCount} open sessions, {SettledCount} settled")]
    private static partial void LogDone(ILogger logger, int openCount, int settledCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Startup check failed for session {SessionId}: {ErrorType}")]
    private static partial void LogSessionFailed(ILogger logger, string sessionId, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup check failed: {ErrorType}")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
