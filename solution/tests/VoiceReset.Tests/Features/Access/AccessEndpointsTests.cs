using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using VoiceReset.Features.Access;

namespace VoiceReset.Tests.Features.Access;

public sealed class AccessEndpointsTests : IAsyncDisposable
{
    private const string TestCode = "test-access-code-123";

    private readonly WebApplicationFactory<Program> _baseFactory = new();
    private readonly HttpClient _client;

    public AccessEndpointsTests()
    {
        // One host per test (fresh rate-limit counters). https, so the Secure cookie is realistic.
        var factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Access:Code"] = TestCode })));
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Disposing the base factory also disposes the derived one.
    public ValueTask DisposeAsync() => _baseFactory.DisposeAsync();

    [Fact]
    public async Task PostAccess_WrongCode_ReturnsOkFalseAndNoCookie()
    {
        // Act
        using var response = await _client.PostAsJsonAsync("/access", new { code = "wrong-code-123456" }, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task PostAccess_RightCode_SetsTheAccessCookie()
    {
        // Act
        using var response = await _client.PostAsJsonAsync("/access", new { code = TestCode }, Ct);

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(body.GetProperty("ok").GetBoolean());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{AccessGate.CookieName}=", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestCode, cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatus_BeforeAndAfterSignIn_ReflectsTheCookie()
    {
        // Arrange
        var before = await _client.GetFromJsonAsync<JsonElement>("/access/status", Ct);
        using var signIn = await _client.PostAsJsonAsync("/access", new { code = TestCode }, Ct);
        var cookie = signIn.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        using var request = new HttpRequestMessage(HttpMethod.Get, "/access/status");
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await _client.SendAsync(request, Ct);
        var after = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        // Assert
        Assert.False(before.GetProperty("signedIn").GetBoolean());
        Assert.True(after.GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task PostAccess_TooManyAttempts_ReturnsOkWithTooManyAttempts()
    {
        // Arrange: five attempts per minute are allowed
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await _client.PostAsJsonAsync("/access", new { code = "wrong-code-123456" }, Ct);
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        }

        // Act: even the right code is refused now
        using var response = await _client.PostAsJsonAsync("/access", new { code = TestCode }, Ct);

        // Assert: 200, not 429, so the browser console stays clean
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("tooManyAttempts").GetBoolean());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task PostAccess_MissingCode_ReturnsOkFalse()
    {
        // Act
        using var response = await _client.PostAsJsonAsync("/access", new { }, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(body.GetProperty("ok").GetBoolean());
    }
}
