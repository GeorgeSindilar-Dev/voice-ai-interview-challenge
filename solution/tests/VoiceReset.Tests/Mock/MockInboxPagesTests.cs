using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceReset.Tests.Mock;

public sealed class MockInboxPagesTests
{
    private const string AlexInboxPassword = "dev-only-inbox-alex"; // appsettings.Development.json
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_WrongPassword_StaysSignedOut()
    {
        await using var app = new MockAppFactory();
        using var browser = CreateBrowser(app);

        using var login = await LoginAsync(browser, "alex.morgan", "wrong-password");
        var html = await login.Content.ReadAsStringAsync(Ct);
        using var inbox = await browser.GetAsync("/mock/inbox", Ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("data-testid=\"login-error\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, inbox.StatusCode); // no cookie: challenged to the login page
    }

    [Fact]
    public async Task Inbox_SignedInUser_SeesOnlyOwnMessages()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        await app.StartAsync(service, "alex.morgan", Ct);
        await app.StartAsync(service, "jamie.lee", Ct);
        var alexMessage = Assert.Single(await app.Issuer.GetInboxAsync("alex.morgan", Ct));
        var jamieMessage = Assert.Single(await app.Issuer.GetInboxAsync("jamie.lee", Ct));
        using var browser = CreateBrowser(app);

        using var login = await LoginAsync(browser, "alex.morgan", AlexInboxPassword);
        var html = await browser.GetStringAsync("/mock/inbox", Ct);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains(alexMessage.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(jamieMessage.Id, html, StringComparison.Ordinal);
    }

    // HTTPS so the Secure __Host- cookie is kept; no auto-redirect so the tests see 302s.
    private static HttpClient CreateBrowser(MockAppFactory app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient browser, string username, string password)
    {
        var page = await browser.GetStringAsync("/mock/inbox/login", Ct); // also sets the antiforgery cookie
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        });
        return await browser.PostAsync("/mock/inbox/login", form, Ct);
    }
}
