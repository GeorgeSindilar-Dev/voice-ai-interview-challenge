using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using VoiceReset.Health;

namespace VoiceReset.Tests.Health;

public sealed class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("1.0.0+abc123", "abc123")]
    [InlineData("1.0.0", "unknown")]
    [InlineData("1.0.0+", "unknown")]
    [InlineData(null, "unknown")]
    public void CommitFrom_InformationalVersion_ReturnsPartAfterPlusOrUnknown(string? version, string expected)
    {
        // Act
        var commit = HealthEndpoints.CommitFrom(version);

        // Assert
        Assert.Equal(expected, commit);
    }

    [Fact]
    public async Task GetHealth_Always_ReturnsOkWithStatusAndCommit()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("commit").GetString()));
    }
}
