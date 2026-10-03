namespace VoiceReset.Mock;

public sealed record StartRecoveryRequest(string Username, string RequestId);
public sealed record VerifyRequest(string Code);
public sealed record ResetLinkRequest(string OperationId);
public sealed record RecoveryAccepted(string RecoveryId, string Status, DateTimeOffset VerificationExpiresAt, int AttemptsRemaining);
public sealed record VerifyResponse(string RecoveryId, string Status, int AttemptsRemaining);
public sealed record ResetLinkResponse(string RecoveryId, string Status, DateTimeOffset? LinkExpiresAt);
public sealed record RecoveryView(string RecoveryId, string Status, DateTimeOffset VerificationExpiresAt, int AttemptsRemaining,
    DateTimeOffset? LinkExpiresAt, string? ResetOperationId, string? ResetReceipt, string? UnlockStatus);
public sealed record PolicyRule(string Code, string Description);
public sealed record PolicyResponse(string PolicyVersion, IReadOnlyList<PolicyRule> Rules);
public sealed record ValidatePasswordRequest(string Token, string Password);
public sealed record ValidatePasswordResponse(bool Valid, string PolicyVersion, IReadOnlyList<PolicyRule> Violations);
public sealed record ResetRequest(string Token, string NewPassword, string OperationId);
public sealed record ResetResponse(string OperationId, string Status, string? ResetReceipt, string? UnlockStatus, string? ReasonCode);
public sealed record CreateTicketRequest(string RecoveryId, string OperationId);
public sealed record TicketResponse(string TicketId, string RecoveryId, string Outcome);
public sealed record TicketOutcomeRequest(string Outcome, string? ResetReceipt, string ReasonCode, string OperationId);
public sealed record TicketOutcomeResponse(string TicketId, string RecoveryId, string Outcome, string? ResetReceipt, string? ReasonCode);
