namespace VoiceReset.Voice;

/// <summary>Call events: IDs, reasons, statuses, codes and exception types only; never transcript text, tool arguments or audio.</summary>
internal static partial class VoiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "CallStarted {SessionId}")]
    public static partial void CallStarted(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "CallEnded {SessionId} {Reason} {DurationSeconds}")]
    public static partial void CallEnded(ILogger logger, string sessionId, string reason, int durationSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "ToolCalled {SessionId} {Tool} {Status}")]
    public static partial void ToolCalled(ILogger logger, string sessionId, string tool, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ResponseNotCompleted {SessionId} {Status}")]
    public static partial void ResponseNotCompleted(ILogger logger, string sessionId, string? status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VoiceLiveError {SessionId} {Code} {Param}")]
    public static partial void VoiceLiveError(ILogger logger, string sessionId, string? code, string? param);

    // Exception messages can carry service response bodies, so only the type is logged.
    [LoggerMessage(Level = LogLevel.Information, Message = "CallConnectionClosed {SessionId} {ExceptionType}")]
    public static partial void ConnectionClosed(ILogger logger, string sessionId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "CallStepFailed {SessionId} {ExceptionType}")]
    public static partial void StepFailed(ILogger logger, string sessionId, string exceptionType);
}
