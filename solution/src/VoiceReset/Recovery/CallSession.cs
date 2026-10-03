using System.Text.Json.Serialization;

namespace VoiceReset.Recovery;

[JsonConverter(typeof(JsonStringEnumConverter<RecoveryState>))]
public enum RecoveryState { AwaitingUsername, AwaitingCode, Verified, LinkSent, Completed, Escalated, Cancelled }

/// <summary>One call's backend state, saved after every tool. Never holds a code, token or password.</summary>
public sealed class CallSession
{
    public required string SessionId { get; init; }
    public required string Channel { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    public RecoveryState State { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? Username { get; set; }
    public string? RequestId { get; set; }
    public string? RecoveryId { get; set; }
    public string? VerifyKey { get; set; }
    public string? LinkOperationId { get; set; }
    public string? TicketOperationId { get; set; }
    public string? TicketId { get; set; }
    public string? OutcomeOperationId { get; set; } // each outcome update is a new operation; saved for traceability
    public string? TicketOutcome { get; set; }
    public string? TicketReason { get; set; }
    public string? ResetReceipt { get; set; }
    public string? UnlockStatus { get; set; }
    public string? EndReason { get; set; }

    [JsonIgnore]
    public bool IsOpen => State is RecoveryState.AwaitingUsername or RecoveryState.AwaitingCode
        or RecoveryState.Verified or RecoveryState.LinkSent;
}
