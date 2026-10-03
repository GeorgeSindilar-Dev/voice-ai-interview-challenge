using System.Net;

namespace VoiceReset.Tests.Mock;

public sealed class MockResetEndpointsTests
{
    private const string User = "alex.morgan";
    private const string StrongPassword = "Correct-Horse-42";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reset_SameTokenTwice_OnlyTheFirstOperationSucceeds()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (_, token) = await IssueLinkAsync(app, service);

        var validation = await MockAppFactory.PostAsync(browser, "/mock/v1/password/validate", new { token, password = StrongPassword }, Ct);
        var first = await ResetAsync(browser, token, StrongPassword, "reset-1");
        var retry = await ResetAsync(browser, token, StrongPassword, "reset-1");
        var changedRetry = await ResetAsync(browser, token, "Another-Horse-43", "reset-1");
        var other = await ResetAsync(browser, token, "Another-Horse-43", "reset-2");

        Assert.True(validation.Body.GetProperty("valid").GetBoolean()); // validating does not consume the token
        Assert.Equal((HttpStatusCode.OK, "succeeded", "not_required"),
            (first.Status, first.Body.GetProperty("status").GetString(), first.Body.GetProperty("unlock_status").GetString()));
        Assert.Equal(first.Body.GetProperty("reset_receipt").GetString(), retry.Body.GetProperty("reset_receipt").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_conflict"), (changedRetry.Status, changedRetry.Error));
        Assert.Equal((HttpStatusCode.Conflict, "token_used"), (other.Status, other.Error));
    }

    [Fact]
    public async Task CheckPassword_AfterReset_AcceptsOnlyTheNewPassword()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (_, token) = await IssueLinkAsync(app, service);
        var initialBefore = await app.Issuer.CheckPasswordAsync(User, "Dev-only-Alex-1", Ct); // appsettings.Development.json

        await ResetAsync(browser, token, StrongPassword, "reset-1");

        Assert.True(initialBefore);
        Assert.False(await app.Issuer.CheckPasswordAsync(User, "Dev-only-Alex-1", Ct));
        Assert.True(await app.Issuer.CheckPasswordAsync(User, StrongPassword, Ct));
        Assert.False(await app.Issuer.CheckPasswordAsync("nobody", StrongPassword, Ct));
    }

    [Fact]
    public async Task Reset_AfterTenMinutes_ReturnsLinkExpired()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (_, token) = await IssueLinkAsync(app, service);

        app.Clock.Advance(TimeSpan.FromMinutes(10));
        var reply = await ResetAsync(browser, token, StrongPassword, "reset-1");

        Assert.Equal((HttpStatusCode.Gone, "link_expired"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task Reset_WeakPassword_Returns422WithoutEchoingIt()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (_, token) = await IssueLinkAsync(app, service);

        var reply = await ResetAsync(browser, token, "Tiny9", "reset-1");

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "policy_violation"), (reply.Status, reply.Error));
        Assert.Contains(reply.Body.GetProperty("violations").EnumerateArray(), v => v.GetProperty("code").GetString() == "min_length");
        Assert.DoesNotContain("Tiny9", reply.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TicketOutcome_ResolvedWithoutMatchingReceipt_ReturnsInvalidState()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        var (recoveryId, _) = await app.StartAsync(service, User, Ct);
        var ticketId = await CreateTicketAsync(service, recoveryId);

        var reply = await SetOutcomeAsync(service, ticketId, "resolved", "rcpt_fabricated", "reset_completed", "o-1");

        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task TicketOutcome_AfterResolved_IsNotOverwritten()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (recoveryId, token) = await IssueLinkAsync(app, service);
        var receipt = (await ResetAsync(browser, token, StrongPassword, "reset-1")).Body.GetProperty("reset_receipt").GetString();
        var ticketId = await CreateTicketAsync(service, recoveryId);

        var resolved = await SetOutcomeAsync(service, ticketId, "resolved", receipt, "reset_completed", "o-1");
        var cancelled = await SetOutcomeAsync(service, ticketId, "cancelled", null, "caller_cancelled", "o-2");

        Assert.Equal((HttpStatusCode.OK, "resolved"), (resolved.Status, resolved.Body.GetProperty("outcome").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (cancelled.Status, cancelled.Error));
    }

    [Fact]
    public async Task TicketOutcome_HumanRequestedEscalation_IsNotChangedByLaterOutcomes()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        using var browser = app.CreateClient();
        var (recoveryId, token) = await IssueLinkAsync(app, service);
        var receipt = (await ResetAsync(browser, token, StrongPassword, "reset-1")).Body.GetProperty("reset_receipt").GetString();
        var ticketId = await CreateTicketAsync(service, recoveryId);

        await SetOutcomeAsync(service, ticketId, "escalated", null, "human_requested", "o-1");
        var resolved = await SetOutcomeAsync(service, ticketId, "resolved", receipt, "reset_completed", "o-2");

        var cancelled = await SetOutcomeAsync(service, ticketId, "cancelled", null, "call_dropped", "o-3");

        foreach (var reply in new[] { resolved, cancelled })
        {
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(("escalated", "human_requested"),
                (reply.Body.GetProperty("outcome").GetString(), reply.Body.GetProperty("reason_code").GetString()));
        }
    }

    /// <summary>Start, verify and issue a link for the user; read the token from the link's fragment.</summary>
    private static async Task<(string RecoveryId, string Token)> IssueLinkAsync(MockAppFactory app, HttpClient service)
    {
        var (recoveryId, code) = await app.StartAsync(service, User, Ct);
        await MockAppFactory.VerifyAsync(service, recoveryId, code, "k1", Ct);
        await MockAppFactory.PostAsync(service, $"/mock/v1/recoveries/{recoveryId}/reset-link", new { operation_id = "link-1" }, Ct);
        var link = (await app.Issuer.GetInboxAsync(User, Ct))[0].Link ?? "";
        return (recoveryId, Uri.UnescapeDataString(link.Split("#token=")[1]));
    }

    private static Task<Reply> ResetAsync(HttpClient browser, string token, string password, string operationId) =>
        MockAppFactory.PostAsync(browser, "/mock/v1/resets", new { token, new_password = password, operation_id = operationId }, Ct);

    private static async Task<string> CreateTicketAsync(HttpClient service, string recoveryId) =>
        (await MockAppFactory.PostAsync(service, "/mock/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-1" }, Ct))
            .Body.GetProperty("ticket_id").GetString() ?? "";

    private static Task<Reply> SetOutcomeAsync(HttpClient service, string ticketId, string outcome, string? receipt, string reason, string operationId) =>
        MockAppFactory.PostAsync(service, $"/mock/v1/tickets/{ticketId}/outcome",
            new { outcome, reset_receipt = receipt, reason_code = reason, operation_id = operationId }, Ct);
}
