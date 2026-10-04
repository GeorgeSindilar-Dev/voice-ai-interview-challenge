using System.Text.Encodings.Web;
using System.Text.Json;
using VoiceReset.Features.Recovery;

namespace VoiceReset.Features.Voice;

/// <summary>
/// Turns one function call from the model into one RecoveryWorkflow call: the model proposes, the backend
/// decides. The session ID comes from the connection, never from the model. Broken JSON, an unknown tool or
/// an unexpected argument gives "invalid_argument" without touching the workflow; a workflow failure gives
/// "unavailable", so no exception reaches the voice session. Arguments are never logged: they hold usernames and codes.
/// </summary>
public sealed partial class ToolDispatcher(RecoveryWorkflow workflow, ILogger<ToolDispatcher> logger)
{
    public const string InvalidArgumentStatus = "invalid_argument";
    public const string CallEndingStatus = "call_ending";
    public const string AgentEndedReason = "agent_ended";

    private static readonly ToolResult s_invalidArgument = new(false, InvalidArgumentStatus, Phrases.InvalidArgument);
    private static readonly ToolResult s_unavailable = new(false, RecoveryWorkflow.UnavailableStatus, Phrases.NotAvailableNow);
    private static readonly ToolResult s_callEnding = new(true, CallEndingStatus, Phrases.Goodbye);
    private static readonly JsonDocumentOptions s_parseOptions = new() { AllowDuplicateProperties = false };
    private static readonly JsonSerializerOptions s_outputOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<ToolResult> DispatchAsync(string sessionId, string toolName, string argumentsJson, CancellationToken ct)
    {
        if (!TryParseObject(argumentsJson, out var args))
        {
            return s_invalidArgument;
        }

        try
        {
            return toolName switch
            {
                ToolDefinitions.StartRecovery when HasOnly(args, "username") =>
                    await workflow.StartRecoveryAsync(sessionId, StringOrNull(args, "username"), ct),
                ToolDefinitions.SubmitCode when HasOnly(args, "code") =>
                    await workflow.SubmitCodeAsync(sessionId, StringOrNull(args, "code"), ct),
                ToolDefinitions.SendResetLink when HasOnly(args) => await workflow.SendResetLinkAsync(sessionId, ct),
                ToolDefinitions.CheckResetStatus when HasOnly(args) => await workflow.CheckResetStatusAsync(sessionId, ct),
                ToolDefinitions.RequestHuman when HasOnly(args) => await workflow.RequestHumanAsync(sessionId, ct),
                ToolDefinitions.CancelReset when HasOnly(args) => await workflow.CancelAsync(sessionId, ct),
                ToolDefinitions.EndCall when HasOnly(args) => await EndCallAsync(sessionId, ct),
                _ => s_invalidArgument,
            };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) // unknown session, store or issuer fault
        {
            LogToolFailed(logger, toolName, sessionId, ex.GetType().Name);
            return s_unavailable;
        }
    }

    /// <summary>The function output the model receives: {"ok":…,"status":"…","say":"…"} (apostrophes kept literal).</summary>
    public static string ToOutputJson(ToolResult result) =>
        JsonSerializer.Serialize(new { ok = result.Ok, status = result.Status, say = result.Say }, s_outputOptions);

    // The call ends whatever happens here: the channel's cleanup calls EndCallAsync again (it is idempotent).
    private async Task<ToolResult> EndCallAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await workflow.EndCallAsync(sessionId, AgentEndedReason, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogToolFailed(logger, ToolDefinitions.EndCall, sessionId, ex.GetType().Name);
        }
        return s_callEnding;
    }

    private static bool TryParseObject(string json, out JsonElement args)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json, s_parseOptions);
            args = document.RootElement.Clone();
            return args.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            args = default;
            return false;
        }
    }

    /// <summary>True when every property is an allowed name. A missing one is checked by the workflow.</summary>
    private static bool HasOnly(JsonElement args, params string[] allowed) =>
        args.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal));

    private static string? StringOrNull(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {Tool} failed for session {SessionId} with {ExceptionType}")]
    private static partial void LogToolFailed(ILogger logger, string tool, string sessionId, string exceptionType);
}
