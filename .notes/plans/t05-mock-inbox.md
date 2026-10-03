# T5: Mock Inbox Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The mock recovery inbox: `/mock/inbox/login` (test username + inbox password) and `/mock/inbox` (that user's
messages only, newest first, auto-refresh every 5 s, logout).

**Architecture:** Two Razor Pages in `Pages/Mock/Inbox/`. Login compares the inbox password from `MockOptions` in
constant time (`MockIssuer.SecretEquals`) and signs in with the cookie scheme `MockInbox` (cookie `__Host-mock-inbox`:
Secure, HttpOnly, SameSite=Strict, path `/`). The inbox page is `[Authorize]`d for that scheme and reads
`MockIssuer.GetInboxAsync(User.Identity.Name)`, so a user can never ask for someone else's messages. Razor Pages
validate antiforgery tokens on every POST; Razor encodes all output. No scripts, no inline styles; `/css/site.css`.

**Tech Stack:** ASP.NET Core Razor Pages, cookie authentication, xUnit v3, `WebApplicationFactory<Program>`. Needs T3.
Commands run from `solution/`. C# blocks omit `using` directives.

**Files:** create `src/VoiceReset/Mock/MockInboxAuth.cs`, `src/VoiceReset/Pages/_ViewImports.cshtml`,
`src/VoiceReset/Pages/Mock/Inbox/Login.cshtml` (+ `.cshtml.cs`), `src/VoiceReset/Pages/Mock/Inbox/Index.cshtml`
(+ `.cshtml.cs`), `src/VoiceReset/wwwroot/css/site.css` (minimal; T12 extends it),
`tests/VoiceReset.Tests/Mock/MockInboxPagesTests.cs`; modify `src/VoiceReset/Program.cs`.

### Task 1: Failing tests
- [ ] **Step 1:** `tests/VoiceReset.Tests/Mock/MockInboxPagesTests.cs`
```csharp
namespace VoiceReset.Tests.Mock;

public sealed class MockInboxPagesTests
{
    private const string AliceInboxPassword = "dev-only-inbox-alice"; // appsettings.Development.json
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_WrongPassword_StaysSignedOut()
    {
        await using var app = new MockAppFactory();
        using var browser = CreateBrowser(app);

        using var login = await LoginAsync(browser, "alice", "wrong-password");
        var html = await login.Content.ReadAsStringAsync(Ct);
        using var inbox = await browser.GetAsync("/mock/inbox", Ct);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("data-testid=\"login-error\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, inbox.StatusCode); // no cookie → challenged to the login page
    }

    [Fact]
    public async Task Inbox_SignedInUser_SeesOnlyOwnMessages()
    {
        await using var app = new MockAppFactory();
        using var service = app.CreateServiceClient();
        await app.StartAsync(service, "alice", Ct);
        await app.StartAsync(service, "bob", Ct);
        var aliceMessage = Assert.Single(await app.Issuer.GetInboxAsync("alice", Ct));
        var bobMessage = Assert.Single(await app.Issuer.GetInboxAsync("bob", Ct));
        using var browser = CreateBrowser(app);

        using var login = await LoginAsync(browser, "alice", AliceInboxPassword);
        var html = await browser.GetStringAsync("/mock/inbox", Ct);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains(aliceMessage.Id, html, StringComparison.Ordinal);
        Assert.DoesNotContain(bobMessage.Id, html, StringComparison.Ordinal);
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
            ["Username"] = username, ["Password"] = password, ["__RequestVerificationToken"] = token,
        });
        return await browser.PostAsync("/mock/inbox/login", form, Ct);
    }
}
```
- [ ] **Step 2:** `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Mock.MockInboxPagesTests"`
  → 2 tests fail: `GET /mock/inbox/login` returns 404, so `GetStringAsync` throws `HttpRequestException`.

### Task 2: Authentication, pages, wiring
- [ ] **Step 1: `src/VoiceReset/Mock/MockInboxAuth.cs`**
```csharp
namespace VoiceReset.Mock;

/// <summary>The mock inbox's own sign-in (separate from the service credential and the agent's access cookie).</summary>
public static class MockInboxAuth
{
    public const string Scheme = "MockInbox";
    public const string DisplayNameClaim = "display_name";

    public static void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "__Host-mock-inbox";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/mock/inbox/login";
        options.AccessDeniedPath = "/mock/inbox/login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    }
}
```
- [ ] **Step 2: `src/VoiceReset/Pages/_ViewImports.cshtml`**
```cshtml
@using System.Globalization
@using VoiceReset.Mock
@namespace VoiceReset.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```
- [ ] **Step 3: `src/VoiceReset/Pages/Mock/Inbox/Login.cshtml.cs`**
```csharp
namespace VoiceReset.Pages.Mock.Inbox;

public sealed class LoginModel(MockIssuer issuer) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public bool Failed { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = ModelState.IsValid ? issuer.FindUser(Username) : null;
        // Always one constant-time comparison, so an unknown username looks like a wrong password.
        var passwordMatches = MockIssuer.SecretEquals(Password ?? "", user?.InboxPassword ?? "");
        if (user is null || !passwordMatches)
        {
            Failed = true;
            return Page();
        }
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, MockIssuer.Normalize(user.Username)), new Claim(MockInboxAuth.DisplayNameClaim, user.DisplayName)],
            MockInboxAuth.Scheme);
        await HttpContext.SignInAsync(MockInboxAuth.Scheme, new ClaimsPrincipal(identity));
        return RedirectToPage("Index");
    }
}
```
(An empty field binds as null and fails the implicit `[Required]` of a non-nullable string: `ModelState.IsValid` guards
`FindUser`, and `Password ?? ""` keeps the comparison safe.)
- [ ] **Step 4: `src/VoiceReset/Pages/Mock/Inbox/Login.cshtml`**
```cshtml
@page
@model VoiceReset.Pages.Mock.Inbox.LoginModel
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <title>Mock inbox – sign in</title>
  <link rel="stylesheet" href="/css/site.css">
</head>
<body>
  <main id="inbox-login" data-testid="inbox-login">
    <h1>Mock recovery inbox</h1>
    <p>Synthetic test inbox. Sign in with your test user's inbox password.</p>
    @if (Model.Failed)
    {
      <p id="login-error" data-testid="login-error" role="alert">Wrong username or inbox password.</p>
    }
    <form method="post" id="login-form" data-testid="login-form">
      <label for="username">Username</label>
      <input id="username" name="Username" value="@Model.Username" autocomplete="username" required data-testid="username">
      <label for="password">Inbox password</label>
      <input id="password" name="Password" type="password" autocomplete="current-password" required data-testid="password">
      <button type="submit" id="login-submit" data-testid="login-submit">Sign in</button>
    </form>
  </main>
</body>
</html>
```
The form tag helper adds the hidden `__RequestVerificationToken`; the password is never written back.
- [ ] **Step 5: `src/VoiceReset/Pages/Mock/Inbox/Index.cshtml.cs`**
```csharp
namespace VoiceReset.Pages.Mock.Inbox;

[Authorize(AuthenticationSchemes = MockInboxAuth.Scheme)]
public sealed class IndexModel(MockIssuer issuer) : PageModel
{
    public IReadOnlyList<InboxMessage> Messages { get; private set; } = [];
    public string DisplayName => User.FindFirst(MockInboxAuth.DisplayNameClaim)?.Value ?? "";

    // The username comes only from the signed-in cookie, never from the request.
    public async Task OnGetAsync(CancellationToken ct) => Messages = await issuer.GetInboxAsync(User.Identity?.Name ?? "", ct);

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(MockInboxAuth.Scheme);
        return RedirectToPage("Login");
    }
}
```
- [ ] **Step 6: `src/VoiceReset/Pages/Mock/Inbox/Index.cshtml`**
```cshtml
@page
@model VoiceReset.Pages.Mock.Inbox.IndexModel
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <meta http-equiv="refresh" content="5">
  <title>Mock inbox</title>
  <link rel="stylesheet" href="/css/site.css">
</head>
<body>
  <header id="inbox-header">
    <h1>Mock inbox: <span id="inbox-user" data-testid="inbox-user">@Model.DisplayName</span></h1>
    <form method="post" asp-page-handler="Logout" id="logout-form">
      <button type="submit" id="logout" data-testid="logout">Sign out</button>
    </form>
  </header>
  <main id="inbox">
    <p>Synthetic messages only. This page refreshes every 5 seconds.</p>
    @if (Model.Messages.Count == 0)
    {
      <p id="inbox-empty" data-testid="inbox-empty">No messages yet.</p>
    }
    else
    {
      <ol id="messages" data-testid="messages">
        @foreach (var message in Model.Messages)
        {
          <li id="@message.Id" data-testid="message">
            <article>
              <h2 data-testid="message-subject">@message.Subject</h2>
              <p><time datetime="@(message.CreatedAt.ToString("O", CultureInfo.InvariantCulture))">@(message.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))</time></p>
              <p data-testid="message-body">@message.Body</p>
              @if (message.Link is not null)
              {
                <p><a href="@message.Link" target="_blank" rel="noopener noreferrer" data-testid="message-link">Open the reset form</a></p>
              }
            </article>
          </li>
        }
      </ol>
    }
  </main>
</body>
</html>
```
- [ ] **Step 7: `src/VoiceReset/wwwroot/css/site.css`** (shared stylesheet; T12 adds the agent page's rules)
```css
:root { color-scheme: light dark; font-family: system-ui, sans-serif; line-height: 1.5; }
body { max-width: 40rem; margin: 2rem auto; padding: 0 1rem; }
form { display: grid; gap: 0.5rem; }
#inbox-header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
#messages { list-style: none; padding: 0; display: grid; gap: 1rem; }
#messages article { border: 1px solid currentColor; border-radius: 0.5rem; padding: 0.75rem 1rem; }
[role="alert"] { font-weight: bold; }
```
- [ ] **Step 8: Wire up `src/VoiceReset/Program.cs`.** Before `builder.Build()` add:
```csharp
builder.Services.AddRazorPages();
builder.Services.AddAuthentication().AddCookie(MockInboxAuth.Scheme, MockInboxAuth.Configure);
builder.Services.AddAuthorization();
```
  After `builder.Build()` (before the `Map...` calls) add `app.UseStaticFiles();`, `app.UseAuthentication();`,
  `app.UseAuthorization();`; after the `Map...` calls add `app.MapRazorPages();`.
- [ ] **Step 9:** Rerun the filtered tests → `Test run summary: Passed!`, total 2, failed 0. Then
  `dotnet build VoiceReset.slnx -c Release` → 0 warnings, and `dotnet test --project tests/VoiceReset.Tests` → total 19, failed 0.
- [ ] **Step 10:** Manual check (no test code): `dotnet run --project src/VoiceReset`, open `/mock/inbox/login`, sign in
  as `alice`; the page is styled, refreshes every 5 s and the browser console is empty.
- [ ] **Step 11:** `git add src/VoiceReset tests/VoiceReset.Tests` and `git commit -m "feat(mock): add inbox login and message pages"`

## Self-review

- Login: username + inbox password from `MockOptions`, constant-time compare, antiforgery (automatic for Razor Pages
  POSTs, token from the form tag helper), scheme `MockInbox`, cookie `__Host-mock-inbox` ✓ (test 1).
- Inbox: own messages only (name from the cookie), newest first (`GetInboxAsync`), meta refresh 5 s, links
  `target="_blank" rel="noopener noreferrer"`, logout, semantic HTML with ids/`data-testid`, no scripts/inline styles,
  `/css/site.css` ✓ (test 2).

## Questions

1. Local `dotnet run` serves plain HTTP on port 5000; browsers keep `Secure` cookies on `http://localhost` (Chrome,
   Edge, Firefox), so it works, but Safari may not. Add an HTTPS launch profile, or document "use Chrome/Edge locally"?
2. Login attempts are not rate-limited (inbox passwords are long random app settings). Fine for the mock?
3. The meta refresh also reloads while someone reads; acceptable, or refresh only when the inbox is empty?

## Additions to contracts

- `MockInboxAuth` (`Scheme = "MockInbox"`, `DisplayNameClaim`, `Configure`); `AddAuthentication()` is called here, so
  T10 adds its `Access` cookie with another `.AddCookie(...)` on the same builder.
- `Program.cs` now has `UseStaticFiles`, `UseAuthentication`, `UseAuthorization`, `AddRazorPages`/`MapRazorPages`.
- `wwwroot/css/site.css` is created here (minimal); T12 extends it rather than replacing it.
