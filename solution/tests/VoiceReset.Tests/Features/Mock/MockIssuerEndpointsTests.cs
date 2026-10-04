using System.Net.Http.Headers;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using VoiceReset.Shared.Storage;

namespace VoiceReset.Tests.Features.Mock;

public sealed class MockIssuerEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartRecovery_WithoutCredential_Returns401()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateClient();
        var reply = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "alex.morgan", request_id = "r1" }, Ct);

        Assert.Equal((HttpStatusCode.Unauthorized, "unauthenticated"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task StartRecovery_UnknownUser_ReturnsSameEnvelopeAndDeliversNothing()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var known = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "alex.morgan", request_id = "r1" }, Ct);
        var unknown = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "nobody", request_id = "r2" }, Ct);

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (known.Status, unknown.Status));
        Assert.Equal(known.Body.EnumerateObject().Select(p => p.Name), unknown.Body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(known.Body.GetProperty("verification_expires_at").GetString(), unknown.Body.GetProperty("verification_expires_at").GetString());
        Assert.Single(await app.Issuer.GetInboxAsync("alex.morgan", Ct));
        Assert.Empty(await app.Issuer.GetInboxAsync("nobody", Ct));
    }

    [Fact]
    public async Task Verify_After120Seconds_ReturnsRecoveryExpired()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alex.morgan", Ct);
        app.Clock.Advance(TimeSpan.FromSeconds(120));
        var reply = await MockAppFactory.VerifyAsync(client, recoveryId, code, "k1", Ct);

        Assert.Equal((HttpStatusCode.Gone, "recovery_expired"), (reply.Status, reply.Error));
    }

    [Fact]
    public async Task Verify_TwoWrongCodes_ExhaustsRecovery()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alex.morgan", Ct);
        var first = await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k1", Ct);
        var second = await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k2", Ct);
        var correctAfterwards = await MockAppFactory.VerifyAsync(client, recoveryId, code, "k3", Ct);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, 1), (first.Status, first.AttemptsRemaining));
        Assert.Equal((HttpStatusCode.Conflict, "verification_exhausted", 0), (second.Status, second.Error, second.AttemptsRemaining));
        Assert.Equal("exhausted", second.Body.GetProperty("status").GetString());
        Assert.Equal("verification_exhausted", correctAfterwards.Error);
    }

    [Fact]
    public async Task Verify_SameIdempotencyKeyTwice_CountsOneAttempt()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alex.morgan", Ct);
        await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k1", Ct);
        var replay = await MockAppFactory.VerifyAsync(client, recoveryId, MockAppFactory.WrongCode(code), "k1", Ct);
        var correct = await MockAppFactory.VerifyAsync(client, recoveryId, code, "k2", Ct);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, 1), (replay.Status, replay.AttemptsRemaining));
        Assert.Equal(HttpStatusCode.OK, correct.Status);
    }

    [Fact]
    public async Task GetInbox_MessagesAtTheSameTime_ReturnsNewestFirst()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, code) = await app.StartAsync(client, "alex.morgan", Ct);
        await MockAppFactory.VerifyAsync(client, recoveryId, code, "k1", Ct);
        await MockAppFactory.PostAsync(client, $"/mock/v1/recoveries/{recoveryId}/reset-link", new { operation_id = "op-1" }, Ct);
        var inbox = await app.Issuer.GetInboxAsync("alex.morgan", Ct);

        Assert.Equal(2, inbox.Count);
        Assert.NotNull(inbox[0].Link); // the reset link, not the code
    }

    [Fact]
    public async Task Restart_KeepsReplaysAndThrottling()
    {
        await using var first = new MockAppFactory();
        using var firstClient = first.CreateServiceClient();
        var (recoveryId, code) = await first.StartAsync(firstClient, "alex.morgan", Ct);
        var wrongCode = MockAppFactory.WrongCode(code);
        await MockAppFactory.VerifyAsync(firstClient, recoveryId, wrongCode, "k1", Ct);

        var store = first.Services.GetRequiredService<IJsonStore>();
        await using var restarted = first.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(store)));
        using var client = restarted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MockAppFactory.ServiceCredential);
        var replay = await MockAppFactory.VerifyAsync(client, recoveryId, wrongCode, "k1", Ct);
        var again = await MockAppFactory.PostAsync(client, "/mock/v1/recoveries", new { username = "alex.morgan", request_id = "other" }, Ct);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, 1), (replay.Status, replay.AttemptsRemaining));
        Assert.Equal((HttpStatusCode.TooManyRequests, "throttled"), (again.Status, again.Error));
        Assert.True(again.RetryAfter > 0);
    }

    [Fact]
    public async Task ResetLink_BeforeVerification_ReturnsInvalidState()
    {
        await using var app = new MockAppFactory();
        using var client = app.CreateServiceClient();
        var (recoveryId, _) = await app.StartAsync(client, "alex.morgan", Ct);
        var reply = await MockAppFactory.PostAsync(client, $"/mock/v1/recoveries/{recoveryId}/reset-link", new { operation_id = "op-1" }, Ct);

        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), (reply.Status, reply.Error));
        Assert.Single(await app.Issuer.GetInboxAsync("alex.morgan", Ct)); // the code only, no link
    }
}
