using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceReset.Tests.Features.Reset;

public sealed class ResetPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] s_ids =
        ["reset-message", "reset-form", "policy-rules", "new-password", "confirm-password", "submit-button"];

    [Fact]
    public async Task GetResetPage_Always_ServesFormWithAllElements()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/reset/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var script = await client.GetAsync("/reset/reset.js", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(s_ids, id => Assert.Contains($"id=\"{id}\"", html));
        Assert.Contains("autocomplete=\"new-password\"", html);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }
}
