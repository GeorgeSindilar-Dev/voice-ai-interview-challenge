namespace VoiceReset.Mock;

public static class MockResetEndpoints
{
    public static IEndpointRouteBuilder MapMockReset(this IEndpointRouteBuilder endpoints)
    {
        // Browser routes: no service credential; the reset token in the body is the only authority.
        var browser = endpoints.MapGroup("/mock/v1");
        browser.MapGet("/policy", () => MockResult.Json(200, new PolicyResponse(PasswordPolicy.Version, PasswordPolicy.Rules)));
        browser.MapPost("/password/validate", ValidatePasswordAsync);
        browser.MapPost("/resets", ResetAsync);

        var tickets = endpoints.MapGroup("/mock/v1/tickets").AddEndpointFilter(MockIssuerEndpoints.RequireServiceCredentialAsync);
        tickets.MapPost("", CreateTicketAsync);
        tickets.MapPost("/{id}/outcome", SetOutcomeAsync);
        return endpoints;
    }

    private static async Task<MockResult> ValidatePasswordAsync(HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<ValidatePasswordRequest>(request, ct) is { } body && !string.IsNullOrWhiteSpace(body.Token)
            ? await issuer.ValidatePasswordAsync(body, ct)
            : MockResult.InvalidRequest();

    private static async Task<MockResult> ResetAsync(HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<ResetRequest>(request, ct) is { } body
            && !string.IsNullOrWhiteSpace(body.Token)
            && !string.IsNullOrWhiteSpace(body.OperationId)
            ? await issuer.ResetPasswordAsync(body, ct)
            : MockResult.InvalidRequest();

    private static async Task<MockResult> CreateTicketAsync(HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<CreateTicketRequest>(request, ct) is { } body
            && !string.IsNullOrWhiteSpace(body.RecoveryId)
            && !string.IsNullOrWhiteSpace(body.OperationId)
            ? await issuer.CreateTicketAsync(body, ct)
            : MockResult.InvalidRequest();

    private static async Task<MockResult> SetOutcomeAsync(string id, HttpRequest request, MockIssuer issuer, CancellationToken ct) =>
        await MockJson.ReadAsync<TicketOutcomeRequest>(request, ct) is { } body
            && !string.IsNullOrWhiteSpace(body.Outcome)
            && !string.IsNullOrWhiteSpace(body.ReasonCode)
            && !string.IsNullOrWhiteSpace(body.OperationId)
            ? await issuer.SetTicketOutcomeAsync(id, body, ct)
            : MockResult.InvalidRequest();
}
