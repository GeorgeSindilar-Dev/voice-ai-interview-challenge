# T10: Access Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Only people who know the access code can open a voice call (Voice Live costs money); the code buys a short-lived cookie that `/voice/ws` requires.

**Architecture:** A cookie authentication scheme named `Access` (cookie `__Host-vr-access`, HttpOnly, Secure, SameSite Strict, 2 hours, no sliding). `POST /access` compares the code in constant time and signs in; a wrong code is an expected failure, so it answers `200 {"ok":false}` (no red line in the browser console). `GET /access/status` lets the page choose between the code form and the Start button. An authorization policy `Access` (scheme `Access`, authenticated user) is what T11 puts on `/voice/ws`. The built-in rate limiter allows 5 code attempts per minute per client IP. The gate is never part of reset authorization.

**Tech Stack:** ASP.NET Core 10 cookie authentication, authorization policies, `Microsoft.AspNetCore.RateLimiting`, options with DataAnnotations validation, xUnit v3, `WebApplicationFactory<Program>`.

**Depends on:** T1 only. All commands run from `solution/`.

---

## File structure

| File | Responsibility |
|---|---|
| `src/VoiceReset/Access/AccessOptions.cs` (create) | `Code`, `AllowedOrigin` (validated at startup) |
| `src/VoiceReset/Access/AccessGate.cs` (create) | Names + `AddAccessGate`: options, cookie scheme, policy, rate limiter |
| `src/VoiceReset/Access/AccessEndpoints.cs` (create) | `POST /access`, `GET /access/status`, `CodeMatches` |
| `src/VoiceReset/Program.cs` (modify) | Wire-up and middleware |
| `src/VoiceReset/appsettings.Development.json` (modify) | Fake local values |
| `tests/VoiceReset.Tests/Access/AccessEndpointsTests.cs` (create) | The three listed tests |

---

### Task 1: Options, scheme, policy and rate limiter

**Files:**
- Create: `src/VoiceReset/Access/AccessOptions.cs`
- Create: `src/VoiceReset/Access/AccessGate.cs`
- Modify: `src/VoiceReset/appsettings.Development.json`

- [ ] **Step 1: Write the options**

`src/VoiceReset/Access/AccessOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace VoiceReset.Access;

/// <summary>Configuration section "Access". The code comes from App Service settings in Azure.</summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    /// <summary>The shared access code for the agent page.</summary>
    [Required, MinLength(12)]
    public string Code { get; set; } = "";

    /// <summary>The page origin, e.g. https://host. T11 puts it in WebSocketOptions.AllowedOrigins.</summary>
    [Required, Url]
    public string AllowedOrigin { get; set; } = "";
}
```

- [ ] **Step 2: Write the gate registration**

`src/VoiceReset/Access/AccessGate.cs`:

```csharp
using System.Threading.RateLimiting;

namespace VoiceReset.Access;

/// <summary>
/// The access-code gate: cookie scheme "Access", authorization policy "Access" (used by /voice/ws)
/// and the rate limit for code attempts. It protects cost only; it never authorizes a reset.
/// </summary>
public static class AccessGate
{
    public const string Scheme = "Access";
    public const string Policy = "Access";
    public const string CookieName = "__Host-vr-access";   // __Host-: Secure, Path=/, no Domain
    public const string RateLimitPolicy = "access-code";

    public static IServiceCollection AddAccessGate(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // No default scheme is set: other features (the mock inbox) have their own cookie scheme.
        services.AddAuthentication()
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(2);
                options.SlidingExpiration = false;
                // An API, not a login page: answer 401/403 instead of redirecting.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Guessing the code: 5 attempts per minute per client IP.
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }
}
```

- [ ] **Step 3: Add fake local values**

In `src/VoiceReset/appsettings.Development.json`, add a top-level section (keep the existing ones):

```json
  "Access": {
    "Code": "dev-only-access-code",
    "AllowedOrigin": "https://localhost:7180"
  }
```

`appsettings.json` gets no `Access` values: the real code and origin are App Service settings (`Access__Code`, `Access__AllowedOrigin`).

- [ ] **Step 4: Build**

Run: `dotnet build VoiceReset.slnx -c Release`
Expected: `Build succeeded.` with `0 Warning(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/VoiceReset/Access/AccessOptions.cs src/VoiceReset/Access/AccessGate.cs src/VoiceReset/appsettings.Development.json
git commit -m "feat(access): add the access cookie scheme, policy and rate limit"
```

---

### Task 2: Endpoints

**Files:**
- Create: `src/VoiceReset/Access/AccessEndpoints.cs`
- Modify: `src/VoiceReset/Program.cs`
- Test: `tests/VoiceReset.Tests/Access/AccessEndpointsTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Tests/Access/AccessEndpointsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using VoiceReset.Access;

namespace VoiceReset.Tests.Access;

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
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Access.AccessEndpointsTests"`
Expected: build FAILS (`AccessGate` not found) if Task 1 is not done; otherwise 3 FAILED with `404 NotFound` / JSON errors.

- [ ] **Step 3: Write the endpoints**

`src/VoiceReset/Access/AccessEndpoints.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace VoiceReset.Access;

public sealed record AccessRequest(string? Code);

public sealed record AccessResult(bool Ok);

public sealed record AccessStatus(bool SignedIn);

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccess(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/access");
        group.MapPost("", SignInAsync).RequireRateLimiting(AccessGate.RateLimitPolicy);
        group.MapGet("/status", GetStatusAsync);
        return endpoints;
    }

    /// <summary>
    /// Exchanges the access code (JSON body, never the URL) for the access cookie. A wrong code is an
    /// expected failure: 200 with ok=false, so the page shows a message and the console stays clean.
    /// </summary>
    public static async Task<Ok<AccessResult>> SignInAsync(AccessRequest request, HttpContext context, IOptions<AccessOptions> options)
    {
        if (!CodeMatches(request.Code, options.Value.Code))
        {
            return TypedResults.Ok(new AccessResult(false));
        }

        // The cookie only says "passed the gate": no name, no personal data.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "agent-page")], AccessGate.Scheme);
        await context.SignInAsync(AccessGate.Scheme, new ClaimsPrincipal(identity));
        return TypedResults.Ok(new AccessResult(true));
    }

    public static async Task<Ok<AccessStatus>> GetStatusAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(AccessGate.Scheme);
        return TypedResults.Ok(new AccessStatus(result.Succeeded));
    }

    /// <summary>Constant-time comparison; hashing first gives both sides the same length.</summary>
    public static bool CodeMatches(string? supplied, string expected) =>
        supplied is not null
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
```

- [ ] **Step 4: Wire it into `Program.cs`**

Add `using VoiceReset.Access;`, then:

```csharp
// services (before builder.Build())
builder.Services.AddAccessGate(builder.Configuration);

// middleware (after builder.Build(), before the Map calls; skip a line another task already added)
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// endpoints
app.MapAccess();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Access.AccessEndpointsTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Full build and test run**

Run: `dotnet build VoiceReset.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test --project tests/VoiceReset.Tests` → all pass (the health tests still pass: Development has `Access` values).

- [ ] **Step 7: Commit**

```bash
git add src/VoiceReset/Access/AccessEndpoints.cs src/VoiceReset/Program.cs tests/VoiceReset.Tests/Access/AccessEndpointsTests.cs
git commit -m "feat(access): exchange the access code for a cookie"
```

---

## Self-review

- Cookie `__Host-vr-access`, HttpOnly, Secure, SameSite Strict, 2 h: Task 1 Step 2, checked by test 2 (except the lifetime, which lives in the ticket).
- `POST /access` JSON `{code}`, constant-time compare, `200 {ok:true|false}`: Task 2 + tests 1 and 2.
- `GET /access/status` → `{signedIn}`: test 3.
- 5/min per IP on `/access`: rate limiter policy on the POST only (the page calls `/access/status` on every load).
- Policy `Access` for `/voice/ws`: registered here, used by T11.

## Questions

1. Behind App Service the client IP is only correct with `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. Without it every caller shares one rate-limit bucket (the front end's IP). T15 should set it. Agree?
2. The cookie is a session cookie (not persistent); the ticket inside expires after 2 hours. Is that fine, or should the browser keep it across restarts (`IsPersistent = true`)?
3. `AllowedOrigin` is a new required setting (dev: `https://localhost:7180`). Alternative: derive it from the request's Host header at runtime (no setting, weaker). Keep the setting?
4. No log line on a rejected code (T14 may add `AccessCodeRejected` without the code or IP). OK?

## Additions to contracts

- `AccessOptions.AllowedOrigin` (config `Access:AllowedOrigin`, required, absolute URL; used by T11 for `WebSocketOptions.AllowedOrigins`). `AccessOptions.SectionName = "Access"`.
- `AccessGate.Scheme = "Access"`, `AccessGate.Policy = "Access"` (use `.RequireAuthorization(AccessGate.Policy)`), `AccessGate.CookieName`, `AccessGate.RateLimitPolicy = "access-code"`; `AddAccessGate(IServiceCollection, IConfiguration)`; `MapAccess()`.
- No default authentication scheme is set by T10; any feature needing auth names its scheme in its policy (T5's `MockInbox` may set itself as default).
- Dev values: `Access:Code = dev-only-access-code`, `Access:AllowedOrigin = https://localhost:7180` (the local HTTPS URL T12's manual check runs on).
- App settings in Azure: `Access__Code`, `Access__AllowedOrigin`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
