namespace VoiceReset.Mock;

public sealed partial class MockIssuer
{
    public Task<MockResult> ValidatePasswordAsync(ValidatePasswordRequest request, CancellationToken ct) =>
        RunAsync(now => ValidatePassword(request, now), save: false, ct);

    public Task<MockResult> ResetPasswordAsync(ResetRequest request, CancellationToken ct) =>
        RunAsync(now => ResetPassword(request, now), save: true, ct);

    private MockResult ValidatePassword(ValidatePasswordRequest request, DateTimeOffset now)
    {
        if (FindByToken(request.Token) is not { } recovery || FindUser(recovery.Username) is not { } user)
        {
            return InvalidToken();
        }
        return TokenRefusal(recovery, now) ?? MockResult.Json(200, Validate(user, request.Password));
    }

    private MockResult ResetPassword(ResetRequest request, DateTimeOffset now)
    {
        if (FindByToken(request.Token) is not { } recovery || FindUser(recovery.Username) is not { } user)
        {
            return InvalidToken();
        }
        if (recovery.ResetOperationId == request.OperationId) // a retry of the accepted operation, even after use or expiry
        {
            // Same operation, same password: the recorded result. A different password is a different request.
            return Hash(request.NewPassword) == _state.PasswordHashes.GetValueOrDefault(recovery.Username)
                ? ResetSucceeded(request.OperationId, recovery)
                : MockResult.IdempotencyConflict();
        }
        if (TokenRefusal(recovery, now) is { } refusal)
        {
            return refusal;
        }
        var validation = Validate(user, request.NewPassword);
        if (!validation.Valid)
        {
            return MockResult.PolicyViolation(validation.Violations); // no reset, the token stays usable
        }
        // The password and any required unlock commit together; only this produces a receipt.
        _state.PasswordHashes[recovery.Username] = Hash(request.NewPassword);
        recovery.ResetOperationId = request.OperationId;
        recovery.ResetReceipt = NewId("rcpt_");
        recovery.UnlockStatus = user.RequiresUnlock ? "unlocked" : "not_required";
        recovery.Status = RecoveryStatus.Completed;
        return ResetSucceeded(request.OperationId, recovery);
    }

    private ValidatePasswordResponse Validate(MockUser user, string password)
    {
        var current = _state.PasswordHashes.GetValueOrDefault(Normalize(user.Username)) ?? Hash(user.InitialPassword);
        var violations = PasswordPolicy.Check(password, user.Username, isCurrentPassword: Hash(password) == current);
        return new ValidatePasswordResponse(violations.Count == 0, PasswordPolicy.Version, violations);
    }

    private MockRecovery? FindByToken(string token)
    {
        var hash = Hash(token);
        return _state.Recoveries.Values.FirstOrDefault(r => r.TokenHash == hash);
    }

    // LinkExpiresAt is set together with TokenHash in SendLink, so a recovery found by token always has it; null would count as not expired.
    private static MockResult? TokenRefusal(MockRecovery recovery, DateTimeOffset now) =>
        recovery.ResetOperationId is not null ? MockResult.Fail(409, "token_used", "This link has already been used.")
        : now >= recovery.LinkExpiresAt ? MockResult.Fail(410, "link_expired", "This link has expired.")
        : null;

    private static MockResult InvalidToken() => MockResult.Fail(401, "invalid_token", "This link is not valid.");

    private static MockResult ResetSucceeded(string operationId, MockRecovery recovery) =>
        MockResult.Json(200, new ResetResponse(operationId, "succeeded", recovery.ResetReceipt, recovery.UnlockStatus, ReasonCode: null));
}
