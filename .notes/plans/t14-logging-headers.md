# T14 Logging, Application Insights, Security Headers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Send API telemetry to Application Insights only when it is configured, keep bodies and secrets out of every log, and add the security headers to all responses.

**Architecture:** Two small pieces in the web app. `ObservabilityExtensions.AddObservability` calls `UseAzureMonitor()` only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. `SecurityHeaders.UseSecurityHeaders` is one inline middleware that sets the headers in `Response.OnStarting`, so they are on every response (static files, endpoints, 404s). Forwarded headers and HSTS are enabled outside Development because the app sits behind the App Service front end.

**Tech Stack:** `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 (already pinned in `Directory.Packages.props`), ASP.NET Core middleware, xUnit v3 + `WebApplicationFactory<Program>`.

**Depends on:** T1. The `/reset/` header test needs the T13 page; the `/mock/inbox` case works even if T5 is missing (the middleware also sets headers on a 404).

**Files:**
- Modify: `solution/src/VoiceReset/VoiceReset.csproj` (package reference)
- Create: `solution/src/VoiceReset/Observability/ObservabilityExtensions.cs`
- Create: `solution/src/VoiceReset/Http/SecurityHeaders.cs`
- Modify: `solution/src/VoiceReset/Program.cs` (wiring, 3 places)
- Modify: `solution/src/VoiceReset/appsettings.json` and `appsettings.Development.json` (log levels)
- Test: `solution/tests/VoiceReset.Tests/Http/SecurityHeadersTests.cs`

---

### Task 1: Failing header tests

- [ ] **Step 1: Write `SecurityHeadersTests.cs`**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceReset.Tests.Http;

public sealed class SecurityHeadersTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/reset/")]
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
```

- [ ] **Step 2: Run, expect FAIL**

Run (from `solution/`): `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Http.SecurityHeadersTests"`
Expected: FAIL (`InvalidOperationException`: the header does not exist).

---

### Task 2: Security headers middleware

- [ ] **Step 1: Create `Http/SecurityHeaders.cs`**

```csharp
namespace VoiceReset.Http;

/// <summary>Adds the same security headers to every response.</summary>
public static class SecurityHeaders
{
    // No inline script or style anywhere: all pages are static HTML + files from the same origin.
    // 'wss:' is for the voice WebSocket to our own host.
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self' wss:; frame-ancestors 'none'; base-uri 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // OnStarting runs just before the first byte is sent, so later middleware
            // (static files, error handling) cannot remove or replace these headers.
            context.Response.OnStarting(static state =>
            {
                Apply((HttpContext)state);
                return Task.CompletedTask;
            }, context);
            return next(context);
        });

    private static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers["Permissions-Policy"] = "microphone=(self)";

        // The reset page and the inbox show or carry secrets (link, code): never cache them.
        var path = context.Request.Path;
        if (path.StartsWithSegments("/reset") || path.StartsWithSegments("/mock/inbox"))
        {
            headers.CacheControl = "no-store";
        }
    }
}
```

- [ ] **Step 2: Wire it in `Program.cs`** (put it before `UseDefaultFiles`/`UseStaticFiles` and before any endpoint mapping; keep the other tasks' lines as they are)

Add `using Microsoft.AspNetCore.HttpOverrides;` and `using VoiceReset.Http;` at the top, then:

```csharp
var builder = WebApplication.CreateBuilder(args);

// ... services from other tasks ...

if (!builder.Environment.IsDevelopment())
{
    // App Service terminates TLS and forwards the original scheme and client address.
    // The app is reachable only through that front end, so every proxy is trusted.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();   // first, so the scheme is right for HSTS, cookies and the WebSocket origin check
    app.UseHsts();
}

app.UseSecurityHeaders();
// app.UseDefaultFiles(); app.UseStaticFiles(); endpoints ... (other tasks)
```

If the compiler reports `KnownIPNetworks` as missing, the SDK in use still has the older name `KnownNetworks`; use whichever one compiles without an obsolete warning (warnings are errors in Release).

- [ ] **Step 3: Run the tests, expect PASS**

Run: `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Http.SecurityHeadersTests"`
Expected: 5 passed. (Without T13 the `/reset/` requests are 404s, which still carry the headers, so the tests pass either way.)

- [ ] **Step 4: Commit**

```bash
git add solution/src/VoiceReset/Http solution/src/VoiceReset/Program.cs solution/tests/VoiceReset.Tests/Http
git commit -m "feat: add security headers, forwarded headers and HSTS"
```

---

### Task 3: Application Insights, only when configured

- [ ] **Step 1: Add the package reference to `VoiceReset.csproj`** (the version is already central):

```xml
  <ItemGroup>
    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" />
  </ItemGroup>
```

- [ ] **Step 2: Create `Observability/ObservabilityExtensions.cs`**

```csharp
using Azure.Monitor.OpenTelemetry.AspNetCore;

namespace VoiceReset.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Exports traces, metrics and logs to Application Insights when
    /// APPLICATIONINSIGHTS_CONNECTION_STRING is set (App Service app setting).
    /// Locally and in tests it is not set, so nothing is exported.
    /// </summary>
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            services.AddOpenTelemetry().UseAzureMonitor();
        }

        return services;
    }
}
```

- [ ] **Step 3: Call it in `Program.cs`** right after `CreateBuilder`:

```csharp
builder.Services.AddObservability(builder.Configuration);
```

What it records, and why that is safe: the ASP.NET Core and HttpClient instrumentation record method, route/path, status code, duration and exceptions, never request or response bodies or headers. Convention for the whole project: no secret in a URL path or query string (the reset token travels in the fragment, which the browser never sends; the issuer calls use IDs only), because paths and queries are recorded.

- [ ] **Step 4: Log levels.** Replace the `Logging` section in `appsettings.json` and `appsettings.Development.json` with:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "System.Net.Http.HttpClient": "Warning",
    "Azure": "Warning"
  }
}
```

`System.Net.Http.HttpClient` at `Information` logs every outgoing URL and at `Trace` the headers; `Warning` keeps it quiet. `Azure` keeps the Azure SDK event-source output (which can include request URLs) quiet.

- [ ] **Step 5: Build, then start with and without the setting**

```bash
dotnet build VoiceReset.slnx -c Release
```
Expected: `0 Warning(s)`.

Run the app twice (`dotnet run --project src/VoiceReset`): once with no setting, once with `APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost.invalid/"` in the environment. Expected both times: `GET /health` returns 200; the second run does not crash (export failures are silent). Stop the app.

- [ ] **Step 6: Commit**

```bash
git add solution/src/VoiceReset
git commit -m "feat: export telemetry to Application Insights when configured"
```

---

### Task 4: Logging hygiene audit

No new code. This is a check on what already exists, to be repeated whenever a task adds logging.

- [ ] **Step 1: Search for body or content logging**

Run (from `solution/`):
```bash
grep -rnE "AddHttpLogging|UseHttpLogging|LoggingContentEnabled|IsLoggingContentEnabled|UseRequestLogging|Console\.Write" src
```
Expected: no matches. (`Azure.Core` content logging is off by default; nobody sets `Diagnostics.IsLoggingContentEnabled = true`.)

- [ ] **Step 2: Search for log calls that could carry secrets**

```bash
grep -rnE "Log(Information|Warning|Error|Debug|Trace)\(|\[LoggerMessage" src
```
Read every hit: the message arguments may be IDs, states, status codes and durations only. Not allowed: tool arguments, transcript text, `code`, `token`, `password`, `Link`, request/response bodies. Fix any violation in the owning file (and mention it in the commit message).

- [ ] **Step 3: Commit only if something was fixed**

```bash
git commit -am "fix: remove sensitive values from logs"
```

---

## Self-review (done)

- Spec: `UseAzureMonitor` only with the connection string; no body logging, no HttpClient body logging, Azure SDK content logging off, levels in appsettings; headers (nosniff, no-referrer, the exact CSP, microphone Permissions-Policy) on all responses; `no-store` on `/reset` and `/mock/inbox`; HSTS + forwarded headers outside Development; tests on `/health` and `/reset/`.
- The CSP string equals the specification character for character.

## Questions

1. `style-src 'self'` and `script-src 'self'` forbid inline code and `style=""` attributes. T5 (Razor inbox pages) and T12 (agent page, AudioWorklet) must not use inline script/style or `blob:` worklets. Confirm they were planned that way; otherwise relax the CSP in one place here.
2. Trusting all proxies for forwarded headers is fine on App Service (the app port is not reachable from outside). Acceptable?
3. Do we want sampling or a daily cap in Application Insights? Not planned (low traffic).

## Additions to contracts

- Namespace `VoiceReset.Http`: `SecurityHeaders.UseSecurityHeaders(this IApplicationBuilder)`; namespace `VoiceReset.Observability`: `ObservabilityExtensions.AddObservability(this IServiceCollection, IConfiguration)`.
- `Program.cs` order: `AddObservability` early in the services; `UseForwardedHeaders`/`UseHsts` (non-Development) then `UseSecurityHeaders` before `UseDefaultFiles`/`UseStaticFiles`, `UseWebSockets` and all endpoints.
- Project rules for every task: never set Azure SDK `Diagnostics.IsLoggingContentEnabled = true`; never put secrets in URL paths or query strings; the pages must not use inline script/style (the CSP blocks them).
- App setting name: `APPLICATIONINSIGHTS_CONNECTION_STRING` (set by T15 from the created Application Insights resource).
