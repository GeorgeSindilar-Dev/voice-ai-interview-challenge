using Microsoft.Extensions.Options;
using VoiceReset.Voice;

namespace VoiceReset.Recovery;

/// <summary>
/// Settles open sessions that have no live call: when the process starts (sessions a previous process left open),
/// then every minute (e.g. a caller who hung up and then finished the reset form).
/// Starts after ApplicationStarted because the issuer is called over HTTP on this same app; never fails the app.
/// </summary>
public sealed partial class OpenSessionCheck(
    SessionStore sessions, IServiceProvider services, IOptions<LimitsOptions> limits, TimeProvider time,
    IHostApplicationLifetime lifetime, ILogger<OpenSessionCheck> logger) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromMinutes(1);
    private readonly DateTimeOffset _processStartedAt = time.GetUtcNow();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WhenStartedAsync(stoppingToken);
        using var timer = new PeriodicTimer(s_interval, time);
        do
        {
            await CheckAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass over the open sessions.</summary>
    public async Task CheckAsync(CancellationToken ct)
    {
        try
        {
            var workflow = services.GetRequiredService<RecoveryWorkflow>(); // resolved after start: the test server's handler can only be created once it is running
            // A session that started before this process, or longer ago than the call limit, has no live call.
            var callLimitAgo = time.GetUtcNow() - TimeSpan.FromSeconds(limits.Value.MaxCallSeconds);
            var noLiveCallBefore = callLimitAgo > _processStartedAt ? callLimitAgo : _processStartedAt;
            var settled = 0;
            foreach (var session in await sessions.ListOpenAsync(ct))
            {
                try
                {
                    if (await workflow.ReconcileAsync(session.SessionId, noLiveCallBefore, ct))
                    {
                        settled++;
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    LogSessionFailed(logger, session.SessionId, ex.GetType().Name);
                }
            }
            if (settled > 0)
            {
                LogSettled(logger, settled);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogFailed(logger, ex.GetType().Name); // e.g. storage unreachable: try again next time
        }
    }

    private async Task WhenStartedAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Open session check: {SettledCount} settled")]
    private static partial void LogSettled(ILogger logger, int settledCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Open session check failed for session {SessionId}: {ErrorType}")]
    private static partial void LogSessionFailed(ILogger logger, string sessionId, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Open session check failed: {ErrorType}")]
    private static partial void LogFailed(ILogger logger, string errorType);
}
