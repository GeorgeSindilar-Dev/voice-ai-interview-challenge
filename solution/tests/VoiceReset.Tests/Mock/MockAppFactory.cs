using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Mock;

namespace VoiceReset.Tests.Mock;

public sealed class MockAppFactory : WebApplicationFactory<Program> // one app per test, Development settings, fake clock
{
    public const string ServiceCredential = "dev-only-service-credential"; // appsettings.Development.json
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public MockIssuer Issuer => Services.GetRequiredService<MockIssuer>();
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceCredential);
        return client;
    }
    public async Task<(string RecoveryId, string Code)> StartAsync(HttpClient client, string username, CancellationToken ct)
    {
        var reply = await PostAsync(client, "/mock/v1/recoveries", new { username, request_id = $"req-{username}" }, ct);
        var message = (await Issuer.GetInboxAsync(username, ct))[0];
        return (reply.Body.GetProperty("recovery_id").GetString() ?? "", Regex.Match(message.Body, @"\d{6}").Value);
    }
    public static Task<Reply> PostAsync(HttpClient client, string url, object body, CancellationToken ct) => SendAsync(client, url, body, null, ct);
    public static Task<Reply> VerifyAsync(HttpClient client, string recoveryId, string code, string key, CancellationToken ct) =>
        SendAsync(client, $"/mock/v1/recoveries/{recoveryId}/verify", new { code }, key, ct);
    public static string WrongCode(string code) => code == "000000" ? "111111" : "000000";
    private static async Task<Reply> SendAsync(HttpClient client, string url, object body, string? idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey); // ignored by routes other than verify
        using var response = await client.SendAsync(request, ct);
        return new Reply(response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(ct),
            (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds);
    }
}

public sealed record Reply(HttpStatusCode Status, JsonElement Body, int? RetryAfter = null)
{
    public string? Error => Body.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;
    public int AttemptsRemaining => Body.GetProperty("attempts_remaining").GetInt32();
}
