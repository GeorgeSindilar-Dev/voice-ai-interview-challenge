using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceReset.Tests.Http;

public sealed class SecurityHeadersTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/reset/")]
    [InlineData("/mock/inbox/login")]
    public async Task Get_AnyPath_HasSecurityHeaders(string path)
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("microphone=(self)", Header(response, "Permissions-Policy"));
        var csp = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("connect-src 'self' wss:", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
    }

    [Theory]
    [InlineData("/reset/", true)]
    [InlineData("/mock/inbox", true)]
    [InlineData("/health", false)]
    public async Task Get_Path_SetsNoStoreOnlyOnSensitivePages(string path, bool expectedNoStore)
    {
        // Arrange (no redirects: the headers of the first response are what counts)
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedNoStore, response.Headers.CacheControl?.NoStore ?? false);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        string.Join(",", response.Headers.GetValues(name));
}
