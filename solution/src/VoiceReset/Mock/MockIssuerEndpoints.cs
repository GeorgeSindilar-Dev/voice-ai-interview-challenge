using Microsoft.AspNetCore.Mvc;

namespace VoiceReset.Mock;

public static class MockIssuerEndpoints
{
    public static IEndpointRouteBuilder MapMockIssuer(this IEndpointRouteBuilder endpoints)
    {
        var recoveries = endpoints.MapGroup("/mock/v1/recoveries").AddEndpointFilter(RequireServiceCredentialAsync);
        recoveries.MapPost("", StartAsync);
        recoveries.MapPost("/{id}/verify", VerifyAsync);
        recoveries.MapPost("/{id}/reset-link", ResetLinkAsync);
        recoveries.MapGet("/{id}", (string id, MockIssuer issuer, CancellationToken ct) => issuer.GetRecoveryAsync(id, ct));
        return endpoints;
    }

    public static async ValueTask<object?> RequireServiceCredentialAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var expected = $"Bearer {http.RequestServices.GetRequiredService<MockIssuer>().ServiceCredential}";
        return MockIssuer.SecretEquals(http.Request.Headers.Authorization.ToString(), expected)
            ? await next(context)
            : MockResult.Unauthenticated();
    }

    private static async Task<MockResult> StartAsync(HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<StartRecoveryRequest>(request, ct) is { } body
            && !string.IsNullOrWhiteSpace(body.Username)
            && !string.IsNullOrWhiteSpace(body.RequestId)
            ? await issuer.StartRecoveryAsync(body, ct)
            : MockResult.InvalidRequest();

    // A malformed request (bad JSON, blank code, no key) is not a completed submission: it never counts as an attempt.
    private static async Task<MockResult> VerifyAsync(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        HttpRequest request,
        MockIssuer issuer,
        CancellationToken ct) =>
        await MockJson.ReadAsync<VerifyRequest>(request, ct) is { } body
            && !string.IsNullOrWhiteSpace(body.Code)
            && !string.IsNullOrWhiteSpace(idempotencyKey)
            ? await issuer.VerifyAsync(id, idempotencyKey, body, ct)
            : MockResult.InvalidRequest();

    private static async Task<MockResult> ResetLinkAsync(string id, HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<ResetLinkRequest>(request, ct) is { } body && !string.IsNullOrWhiteSpace(body.OperationId)
            ? await issuer.IssueResetLinkAsync(id, body, ct)
            : MockResult.InvalidRequest();
}
