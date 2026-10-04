using System.Globalization;
using System.Text.Json;

namespace VoiceReset.Features.Mock;

// Body is a snake_case JsonElement, so it is stored as-is in Replays (the store's camelCase options don't touch it).
public sealed record MockResult(int StatusCode, JsonElement Body, int? RetryAfterSeconds = null) : IResult
{
    public static MockResult Json(int statusCode, object body) => new(statusCode, JsonSerializer.SerializeToElement(body, MockJson.Options));
    public static MockResult Fail(int statusCode, string code, string message) => Json(statusCode, new ErrorBody(new(code, message)));
    public static MockResult VerifyFail(int statusCode, string code, string message, int attemptsRemaining, string status) =>
        Json(statusCode, new VerifyErrorBody(new(code, message), attemptsRemaining, status));

    // Only safe rule codes and descriptions, never the password.
    public static MockResult PolicyViolation(IReadOnlyList<PolicyRule> violations) =>
        Json(422, new PolicyErrorBody(new("policy_violation", "The password does not meet the policy."), violations));

    public static MockResult InvalidRequest() => Fail(400, "invalid_request", "The request is not valid.");
    public static MockResult Unauthenticated() => Fail(401, "unauthenticated", "Authentication is required.");
    public static MockResult NotFound() => Fail(404, "not_found", "The resource was not found.");
    public static MockResult InvalidState() => Fail(409, "invalid_state", "The operation is not allowed in the current state.");
    public static MockResult IdempotencyConflict() => Fail(409, "idempotency_conflict", "The key was used for a different request.");
    public static MockResult Throttled(TimeSpan retryAfter) =>
        Fail(429, "throttled", "Too many requests. Try again later.") with { RetryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds) };

    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (RetryAfterSeconds is { } seconds)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }
        httpContext.Response.StatusCode = StatusCode;
        return httpContext.Response.WriteAsJsonAsync(Body, MockJson.Options, httpContext.RequestAborted);
    }

    private sealed record ErrorDetail(string Code, string Message);
    private sealed record ErrorBody(ErrorDetail Error);
    private sealed record PolicyErrorBody(ErrorDetail Error, IReadOnlyList<PolicyRule> Violations);
    private sealed record VerifyErrorBody(ErrorDetail Error, int AttemptsRemaining, string Status);
}
