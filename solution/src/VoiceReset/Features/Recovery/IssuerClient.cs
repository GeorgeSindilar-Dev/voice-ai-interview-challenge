using System.Net.Http.Json;
using System.Text.Json;

namespace VoiceReset.Features.Recovery;

public enum IssuerOutcome { Success, Error, Unavailable }

/// <summary>Success has a Value; Error has the issuer's error code; Unavailable = outcome unknown (timeout, 5xx, network).</summary>
public sealed record IssuerResult<T>(IssuerOutcome Outcome, T? Value, string? ErrorCode) where T : class;

public sealed record RecoveryStarted(string RecoveryId, string Status);
public sealed record VerifyResult(string RecoveryId, string Status);
public sealed record LinkIssued(string RecoveryId, string Status, DateTimeOffset LinkExpiresAt);
public sealed record RecoveryStatus(string RecoveryId, string Status, string? ResetReceipt, string? UnlockStatus);
public sealed record TicketInfo(string TicketId, string RecoveryId, string Outcome, string? ReasonCode);

/// <summary>Typed client for the issuer's service routes (v1/...), authenticated with the service credential.</summary>
public sealed class IssuerClient(HttpClient http)
{
    private const int FirstServerErrorStatus = 500;

    // Only the fields we use are read; others are ignored.
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task<IssuerResult<RecoveryStarted>> StartRecoveryAsync(string username, string requestId, CancellationToken ct) =>
        SendAsync<RecoveryStarted>(HttpMethod.Post, "v1/recoveries", new { username, request_id = requestId }, null, ct);

    public Task<IssuerResult<VerifyResult>> VerifyAsync(string recoveryId, string code, string idempotencyKey, CancellationToken ct) =>
        SendAsync<VerifyResult>(HttpMethod.Post, $"v1/recoveries/{Uri.EscapeDataString(recoveryId)}/verify", new { code }, idempotencyKey, ct);

    public Task<IssuerResult<LinkIssued>> SendResetLinkAsync(string recoveryId, string operationId, CancellationToken ct) =>
        SendAsync<LinkIssued>(HttpMethod.Post, $"v1/recoveries/{Uri.EscapeDataString(recoveryId)}/reset-link",
            new { operation_id = operationId }, null, ct);

    public Task<IssuerResult<RecoveryStatus>> GetRecoveryAsync(string recoveryId, CancellationToken ct) =>
        SendAsync<RecoveryStatus>(HttpMethod.Get, $"v1/recoveries/{Uri.EscapeDataString(recoveryId)}", null, null, ct);

    public Task<IssuerResult<TicketInfo>> CreateTicketAsync(string recoveryId, string operationId, CancellationToken ct) =>
        SendAsync<TicketInfo>(HttpMethod.Post, "v1/tickets", new { recovery_id = recoveryId, operation_id = operationId }, null, ct);

    public Task<IssuerResult<TicketInfo>> SetTicketOutcomeAsync(
        string ticketId, string outcome, string? receipt, string reasonCode, string operationId, CancellationToken ct) =>
        SendAsync<TicketInfo>(HttpMethod.Post, $"v1/tickets/{Uri.EscapeDataString(ticketId)}/outcome",
            new { outcome, reset_receipt = receipt, reason_code = reasonCode, operation_id = operationId }, null, ct);

    private async Task<IssuerResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
        where T : class
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: s_json) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        try
        {
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode >= FirstServerErrorStatus)
            {
                return Unavailable<T>();
            }
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(s_json, ct);
                return value is null ? Unavailable<T>() : new(IssuerOutcome.Success, value, null);
            }
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(s_json, ct);
            return new(IssuerOutcome.Error, null, error?.Error?.Code ?? "unknown");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return Unavailable<T>(); // the issuer may or may not have acted
        }
    }

    private static IssuerResult<T> Unavailable<T>() where T : class => new(IssuerOutcome.Unavailable, null, null);

    private sealed record ErrorResponse(ErrorDetail? Error);

    private sealed record ErrorDetail(string Code);
}
