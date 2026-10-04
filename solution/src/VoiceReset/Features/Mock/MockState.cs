namespace VoiceReset.Features.Mock;

/// <summary>Everything the mock keeps; saved as one document (mock/state.json).</summary>
public sealed class MockState
{
    public Dictionary<string, MockRecovery> Recoveries { get; set; } = [];
    public List<InboxMessage> Inbox { get; set; } = [];

    /// <summary>Recorded results by key, e.g. "verify:{recoveryId}:{idempotencyKey}".</summary>
    public Dictionary<string, Replay> Replays { get; set; } = [];

    /// <summary>Normalized username to SHA-256 of the current password; absent means the user's InitialPassword.</summary>
    public Dictionary<string, string> PasswordHashes { get; set; } = [];

    public Dictionary<string, MockTicket> Tickets { get; set; } = [];
}

public sealed record TicketUpdate(DateTimeOffset At, string OperationId, string Outcome, string? ReasonCode);

/// <summary>One ticket per recovery, with the history of its outcome updates.</summary>
public sealed class MockTicket
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Escalated = "escalated";
    public const string Cancelled = "cancelled";
    public const string Pending = "pending";

    public const string ResetCompleted = "reset_completed";
    public const string HumanRequested = "human_requested";

    public required string Id { get; init; }
    public required string RecoveryId { get; init; }

    public string Outcome { get; set; } = Open;
    public string? ResetReceipt { get; set; }
    public string? ReasonCode { get; set; }
    public List<TicketUpdate> History { get; set; } = [];
}

public sealed record InboxMessage(string Id, string Username, DateTimeOffset CreatedAt, string Subject, string Body, string? Link);

/// <summary>The recorded result of a request and a hash of that request.</summary>
public sealed record Replay(string RequestHash, MockResult Result);

public static class RecoveryStatus
{
    public const string AwaitingVerification = "awaiting_verification";
    public const string Verified = "verified";
    public const string LinkIssued = "link_issued";
    public const string Completed = "completed";
    public const string Exhausted = "exhausted";
    public const string Expired = "expired";
}

/// <summary>One recovery. Recovery records keep only hashes; the inbox holds the delivered messages (code and link).</summary>
public sealed class MockRecovery
{
    public const int MaxAttempts = 2;

    public required string Id { get; init; }
    public required string Username { get; init; }
    public required string RequestId { get; init; }
    public required DateTimeOffset VerificationExpiresAt { get; init; }

    public string? CodeHash { get; set; } // null for unknown users and once used
    public string Status { get; set; } = RecoveryStatus.AwaitingVerification;
    public int AttemptsRemaining { get; set; } = MaxAttempts;
    public string? LinkOperationId { get; set; }
    public string? TokenHash { get; set; }
    public DateTimeOffset? LinkExpiresAt { get; set; }
    public string? ResetOperationId { get; set; }
    public string? ResetReceipt { get; set; }
    public string? UnlockStatus { get; set; }

    public RecoveryView ToView(DateTimeOffset now) =>
        new(Id, StatusAt(now), VerificationExpiresAt, AttemptsRemaining, LinkExpiresAt, ResetOperationId, ResetReceipt, UnlockStatus);

    public string StatusAt(DateTimeOffset now) => Status switch
    {
        RecoveryStatus.AwaitingVerification or RecoveryStatus.Verified or RecoveryStatus.Exhausted
            when now >= VerificationExpiresAt => RecoveryStatus.Expired,
        RecoveryStatus.LinkIssued when now >= LinkExpiresAt => RecoveryStatus.Expired,
        _ => Status,
    };

    /// <summary>No new recovery for the account before this: the 120 s code window, or link expiry while a link is out.</summary>
    public DateTimeOffset BlockedUntil() =>
        Status == RecoveryStatus.LinkIssued && LinkExpiresAt is { } linkExpiry ? linkExpiry : VerificationExpiresAt;
}
