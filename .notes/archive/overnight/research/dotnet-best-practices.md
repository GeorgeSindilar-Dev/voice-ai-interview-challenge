# .NET best practices for the voice reset agent

Research date: **2026-10-03**. Scope: the coding, testing, and deployment practices
for our two ASP.NET Core apps (`VoiceReset.Agent`, `VoiceReset.Mocks`) and their
tests. Sources are mostly Microsoft Learn, the .NET and Azure SDK GitHub repos, and
xunit.net. Links are listed next to each topic.

How to read this document: section 1 is the short version. Section 2 explains each
recommendation and why. Section 3 lists what we leave out on purpose. Section 4
has files you can copy. Section 5 has the proposed text for `CLAUDE.md`.

Every code snippet in sections 2 and 4 was compiled (and the test snippets run) in a
throw-away solution with the installed .NET 10 SDK, `TreatWarningsAsErrors` and
`AnalysisLevel=latest-recommended`, so they are known to build cleanly.

---

## 1. Summary

- **Platform:** .NET 10 is the current LTS (released November 2025, supported until
  2028-11-14). C# 14 is the current language version and the default for `net10.0`.
  .NET 11 is only at RC1 and is a short-term release, so we stay on .NET 10.
- **Update the local SDK.** The installed SDK is 10.0.302 (runtime 10.0.10, July
  2026). The latest is SDK 10.0.401 / runtime 10.0.12 (2026-09-08, a security
  release). Install it before building the deployed artifacts.
- **Build setup:** one `.slnx` solution (the .NET 10 default), one
  `Directory.Build.props`, Central Package Management (`Directory.Packages.props`),
  a short `.editorconfig`, `AnalysisLevel=latest-recommended`, nullable enabled,
  warnings are errors in Release builds.
- **ASP.NET Core:** Minimal APIs (Microsoft's recommendation for new projects) with
  route groups and `TypedResults`. Options classes validated at startup.
  Typed `HttpClient`s with the standard resilience handler, but **no automatic
  retries on POST** (a retried reset could run twice). Strict JSON (reject unknown
  and duplicate fields, as the mock contract requires). Built-in rate limiter,
  cookie authentication for the access gate, a small security-headers middleware.
- **Time:** inject `TimeProvider` everywhere; tests use `FakeTimeProvider`.
- **Logging:** `ILogger` with source-generated `[LoggerMessage]` methods (the
  analyzer asks for it anyway). Never pass a secret to a logger. Application
  Insights through the Azure Monitor OpenTelemetry distro, authenticated with
  managed identity.
- **Azure SDK:** `ManagedIdentityCredential` in Azure, a developer credential
  locally, never `DefaultAzureCredential` in production. All Azure clients are
  singletons, registered with `AddAzureClients`. Table Storage updates use ETags
  (optimistic concurrency). Secrets come in as App Service Key Vault references.
- **Testing:** xUnit v3 (current version 4.0.1, on Microsoft Testing Platform),
  `WebApplicationFactory` for in-process HTTP tests, hand-written fakes instead of
  a mocking library, `Method_Scenario_Expected` names and Arrange/Act/Assert.
- **Design:** plain classes first. An interface only at an I/O boundary that tests
  must replace. The recovery state machine is a `switch` expression over an enum,
  not a library. Expected failures are return values (a small closed `record`
  hierarchy); exceptions are for bugs and infrastructure faults.
- **Version and deploy:** stamp the git commit into `InformationalVersion` (pass
  `SourceRevisionId` explicitly), show it on `/health`, deploy a framework-dependent
  `dotnet publish` zip with `az webapp deploy`, Always On enabled.

---

## 2. Recommendations per topic

### 2.1 Platform and versions

| Item | Value (2026-10-03) | Source |
| --- | --- | --- |
| Current LTS | .NET 10 (end of support 2028-11-14) | [releases-index.json](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json) |
| Latest .NET 10 patch | runtime 10.0.12, SDK 10.0.401 (2026-09-08, security fixes) | [10.0 releases.json](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) |
| Installed here | SDK 10.0.302 (runtime 10.0.10) | `dotnet --list-sdks` |
| C# version | C# 14 (ships with .NET 10) | [What's new in C# 14](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14) |
| Next release | .NET 11 RC1, an STS release, GA expected Nov 2026 | releases-index.json |
| .NET 8 and 9 | both end support on 2026-11-10 | releases-index.json |

Recommendations:

- Target `net10.0` only. Do not set `LangVersion`; the default for `net10.0` is C# 14.
- Pin the SDK with `global.json` (`rollForward: latestFeature`) so every machine
  and CI uses a .NET 10 SDK, and so `dotnet test` uses Microsoft Testing Platform
  (see 2.7). File in section 4.
- On App Service Linux set the stack to `DOTNETCORE|10.0`. App Service installs
  the runtime patches; we deploy framework-dependent output.

C# 14 features worth knowing (use them only where they make code clearer):
the `field` keyword in property accessors, null-conditional assignment
(`customer?.Order = ...`), extension members (properties too). None is required for
this project.

### 2.2 Solution layout and build configuration

Proposed layout (everything inside `solution/`, as `CLAUDE.md` requires):

```text
solution/
  VoiceReset.slnx
  global.json
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  src/VoiceReset.Agent/        ASP.NET Core app: voice backend, state machine, pages
  src/VoiceReset.Mocks/        ASP.NET Core app: mock issuer, ticket API, inbox
  tests/VoiceReset.Agent.Tests/
  tests/VoiceReset.Mocks.Tests/
```

- **`.slnx`:** since .NET 10, `dotnet new sln` creates the XML `.slnx` format by
  default. It is short, readable and merges well in git. Use it.
  ([breaking change note](https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default))
- **`Directory.Build.props`:** shared MSBuild properties for all projects (target
  framework, nullable, analyzers). Project files then contain only what is special
  about that project.
- **Central Package Management:** `Directory.Packages.props` with
  `ManagePackageVersionsCentrally=true` holds every package version in one place;
  `PackageReference` items have no `Version`. This keeps the two apps and the tests
  on the same versions. Cost: one small file.
  ([MSBuild props](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props))
- **Analyzers:** the .NET SDK analyzers are on by default. Set
  `AnalysisLevel=latest-recommended`. This raises about 145 rules to warnings
  (the default mode has only a few dozen), for example CA1848/CA1873 (use `LoggerMessage`), CA1305 (pass a culture),
  CA1822 (mark members static), CA1707 (no underscores in names, which we switch off
  for test method names). ([code analysis overview](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview))
- **Code style on build:** `EnforceCodeStyleInBuild=true` makes the `IDExxxx` rules
  in `.editorconfig` that we mark as `warning` run during `dotnet build`, not only
  in the IDE.
- **Warnings as errors:** `TreatWarningsAsErrors=true` **only for Release builds**.
  Debug builds stay fast to iterate; `dotnet publish` (Release) and CI cannot ship
  code with warnings, including nullable warnings.
- **No third-party analyzers** (StyleCop, Roslynator, Sonar). The built-in set is
  enough and is what an interviewer from Microsoft will recognise.

### 2.3 C# language and style

Follow the Microsoft C# conventions
([coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions),
[identifier names](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names)):

- **Nullable reference types on.** Treat a nullable warning as a real bug. Avoid the
  `!` (null-forgiving) operator; if you need it, add a comment saying why.
- **File-scoped namespaces** (`namespace VoiceReset.Agent.Sessions;`) and `using`
  directives outside the namespace. Implicit usings on.
- **Naming:** PascalCase for types, methods, properties and constants; camelCase for
  locals and parameters; `_camelCase` for private fields; `s_camelCase` for private
  static fields; interfaces start with `I`; async methods end with `Async`.
- **Records** for immutable data: DTOs, tool arguments, results, events
  (`public sealed record VerifyCodeArgs(string Code);`). Positional record parameters
  are PascalCase because they become properties.
- **Primary constructors** for services that only store their dependencies
  (`public sealed class SessionStore(TableServiceClient tables)`). Parameters are
  camelCase. Remember they are captured parameters, not `readonly` fields: never
  assign to them. If a class does real work in its constructor, use a normal
  constructor.
- **`required` + `init`** for options and entity classes, so a missing value is a
  compile error. (Tested: the configuration binder fills `required` properties.)
- **Collection expressions** (`string[] allowed = ["a", "b"];`, `List<T> items = [];`).
- **`var`** only when the type is obvious from the right side (`new`, a cast, a
  literal). Otherwise write the type; the code must read well on GitHub without
  IDE tooltips.
- **`sealed`** on classes by default (nothing in this project is designed for
  inheritance).
- **Switch expressions and pattern matching** for state transitions and result
  handling (see 2.8).
- **`async` all the way**, always pass a `CancellationToken`, never `.Result` or
  `.Wait()`. No `ConfigureAwait(false)` needed in ASP.NET Core app code.
- **Comments** explain *why*, not *what*. XML doc comments only on non-obvious
  public members; no `GenerateDocumentationFile` (it would add a warning for every
  undocumented public member).

### 2.4 ASP.NET Core

#### Minimal APIs, route groups, TypedResults, endpoint filters

Microsoft now recommends Minimal APIs for new projects
([APIs overview](https://learn.microsoft.com/aspnet/core/fundamentals/apis?view=aspnetcore-10.0)).
Controllers add nothing we need.

- Keep `Program.cs` short: registration + `app.MapXxxEndpoints()` calls. Put each
  area in a static class with one extension method, for example
  `SessionEndpoints.MapSessionEndpoints(this IEndpointRouteBuilder app)`.
- Use **route groups** to share a prefix and policies:
  `var api = app.MapGroup("/api").RequireAuthorization();`
- Return **`TypedResults`** (`TypedResults.Ok(dto)`, `TypedResults.NotFound()`), and
  declare the union in the signature: `Results<Ok<SessionDto>, NotFound>`. It is
  self-documenting and easy to unit test.
- Use **handler methods** (named static methods) instead of long lambdas, so each
  endpoint can be read and tested on its own.
- **Endpoint filters** only for a cross-cutting rule that applies to several
  endpoints (for example "session must exist and belong to this caller"). For one
  endpoint, a plain `if` in the handler is clearer.
- **Request validation:** ASP.NET Core 10 has built-in Minimal API validation
  (`builder.Services.AddValidation()` with data annotations). It is optional; for the
  few DTOs we have, a small explicit check in the handler is just as clear. For
  model tool calls, validate arguments in our own code (allowlists, lengths,
  formats); this is a guardrail and must be visible and tested.

#### Options pattern, validated at startup

([options pattern](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0))

```csharp
public sealed class VoiceLiveOptions
{
    public const string SectionName = "VoiceLive";

    [Required]
    public required Uri Endpoint { get; init; }

    [Range(1, 60)]
    public int MaxCallMinutes { get; init; } = 10;
}

builder.Services.AddOptions<VoiceLiveOptions>()
    .BindConfiguration(VoiceLiveOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

- `ValidateOnStart` makes a missing or wrong setting crash the app at startup with a
  clear message, instead of failing in the middle of a call.
- Inject `IOptions<T>` (values fixed after start). We don't need `IOptionsMonitor`.
- In App Service, nested keys use a double underscore: `VoiceLive__Endpoint`.
- **Key Vault reference trap:** if App Service cannot resolve a Key Vault reference,
  the app receives the literal text `@Microsoft.KeyVault(...)` as the value
  ([docs](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)).
  Add a validation rule that rejects values starting with `@Microsoft.KeyVault(` so
  the app fails at startup instead of using the reference text as the access code.
- **Optional phone channel:** bind ACS settings to their own options class with
  an `Enabled` flag; register the ACS endpoints only when it is set and valid.

#### Dependency injection lifetimes

([DI guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines))

- **Singleton:** stateless services, Azure SDK clients, `TimeProvider`, options,
  the in-memory registry of active calls (if we need one).
- **Scoped:** rarely needed here (no EF Core). Do not use scoped services from a
  WebSocket loop or a `BackgroundService`; they live outside a normal request.
- **Transient:** typed `HttpClient` consumers (registered by `AddHttpClient<T>`).
- **One object per call:** the live voice session (Voice Live connection, state,
  counters) is created with `new` by a factory/singleton when a call starts and
  disposed when it ends. It is not a DI service.
- Never capture a scoped service in a singleton (DI validates this in Development).

#### JSON: strict by default

The mock contract says "reject incorrect field types or unexpected fields". .NET 10
added a `JsonSerializerOptions.Strict` preset with exactly these settings
([.NET 10 libraries](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries)).
For ASP.NET Core endpoints, set the same flags on the web options (and snake_case,
because the contract uses `snake_case` names):

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    JsonSerializerOptions json = options.SerializerOptions;
    json.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    json.AllowDuplicateProperties = false;
    json.RespectNullableAnnotations = true;
    json.RespectRequiredConstructorParameters = true;
});
```

Use one shared, static `JsonSerializerOptions` instance with the same settings for
the typed `HttpClient`s and for parsing model tool arguments. Never create
`JsonSerializerOptions` per call (it is expensive and the analyzer warns).

#### HttpClient: typed clients and resilience

([IHttpClientFactory](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory),
[HTTP resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience))

```csharp
using Microsoft.Extensions.Http.Resilience; // needed for DisableForUnsafeHttpMethods

builder.Services.AddHttpClient<IssuerClient>((services, client) =>
    {
        IssuerOptions options = services.GetRequiredService<IOptions<IssuerOptions>>().Value;
        client.BaseAddress = options.BaseUrl;
    })
    .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
```

- One **typed client** per external API (`IssuerClient`, `TicketClient`). It hides
  URLs, auth headers and JSON, and returns our own result types.
- The **standard resilience handler** adds, in order: rate limiter, 30 s total
  timeout, retry (3 times, exponential backoff with jitter), circuit breaker, 10 s
  per-attempt timeout. Use only one resilience handler per client.
- **Retries vs idempotency:** by default it retries every method.
  `DisableForUnsafeHttpMethods()` stops retries for POST, PUT, PATCH, DELETE. This is
  essential: if "start reset" times out, we **do not know** whether it happened.
  Retrying blindly could create a second operation. Instead return an "unknown"
  result, keep the session in an "outcome unknown" state, and let reconciliation
  ask the issuer what really happened. If the mock contract supports an idempotency
  key, send one and then a retry becomes safe.
- Tune the timeouts to the voice use case (a caller will not wait 30 s): for example
  total 10 s, attempt 4 s.
- Set the issuer's bearer credential in the typed client (from options); never log
  the `Authorization` header.

#### WebSockets

([WebSockets in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0))

```csharp
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20), // ping so proxies keep it open
    KeepAliveTimeout = TimeSpan.FromSeconds(20),  // no pong in time: connection aborted
};
webSocketOptions.AllowedOrigins.Add(publicOrigin); // from configuration
app.UseWebSockets(webSocketOptions);

app.Map("/ws/voice", HandleVoiceSocketAsync).RequireAuthorization();
```

- Use `app.Map` (all methods), not `MapGet`: WebSockets over HTTP/2 use `CONNECT`.
- **Origin check:** browsers do not apply CORS to WebSockets. Set `AllowedOrigins`
  so another site cannot open our socket with the visitor's cookie (cross-site
  WebSocket hijacking). The ACS media socket comes from a server, not a browser; it
  is protected differently (see the phone research).
- **Keep-alive:** the default ping interval is 2 minutes and the pong timeout is off.
  Set both to about 20 s so a dead browser tab or dropped network is detected
  quickly and handled as a dropped call.
- **Keep the request alive:** the endpoint must not return until the socket is
  finished; otherwise sends fail with "response has completed". Await the session
  loop inside the handler.
- **Receive loop** with `ReceiveAsync(Memory<byte>)`, a reused buffer, and the
  request's cancellation token:

```csharp
byte[] buffer = new byte[16 * 1024];
while (socket.State == WebSocketState.Open)
{
    ValueWebSocketReceiveResult result =
        await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);

    if (result.MessageType == WebSocketMessageType.Close)
    {
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
        break;
    }

    // Handle buffer[..result.Count]; check result.EndOfMessage for larger messages.
}
```

- **One sender at a time:** a `WebSocket` allows one `SendAsync` and one
  `ReceiveAsync` at the same time. Audio from Voice Live and control events must
  not call `SendAsync` concurrently. Use a `SemaphoreSlim(1, 1)` around sends, or
  one send loop that reads from a bounded `Channel<T>`.
- **Expected errors:** `WebSocketException` and `OperationCanceledException` mean
  "the caller is gone". Catch them at the loop boundary, end the session as
  *dropped*, and log one line. They are not errors in the logs.
- **No compression** on these sockets (CRIME/BREACH risk with secrets on the wire).
- **Limits:** cap the message size you accept, the call duration, and the number
  of turns; all from options.
- .NET 10 adds `WebSocketStream` (a `Stream` over a socket). It is handy for text
  protocols, but for raw audio frames the plain loop above is simpler to explain.

#### Rate limiting

([rate limiting middleware](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0))

```csharp
using System.Threading.RateLimiting;           // FixedWindowRateLimiterOptions, RateLimitPartition
using Microsoft.AspNetCore.RateLimiting;       // AddPolicy, AddConcurrencyLimiter

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Access-code attempts: a few per minute per client IP.
    options.AddPolicy("access-code", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));

    // Concurrent calls: a WebSocket request lasts the whole call, so this caps live calls (cost).
    options.AddConcurrencyLimiter("voice-calls", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.QueueLimit = 0;
    });
});

app.UseRateLimiter(); // after UseRouting if routing is called explicitly
```

- Apply with `.RequireRateLimiting("access-code")` on the login endpoint and
  `.RequireRateLimiting("voice-calls")` on both audio sockets (browser and ACS), so
  both channels share the same cap.
- The limiter is in memory, per instance. That is fine for one App Service instance;
  write it down as a known limitation.
- Partitioning by IP needs the real client IP: enable forwarded headers (see the
  security section).

#### Cookie authentication for the access gate

([cookie auth](https://learn.microsoft.com/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0))

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-voice-access"; // __Host- = Secure, Path=/, no Domain
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = false;
    });
builder.Services.AddAuthorization();
```

- Compare the code in **constant time**. Hash both sides first so the lengths match:

```csharp
static bool AccessCodeMatches(string supplied, string expected) =>
    CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
        SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
```

- On success call `HttpContext.SignInAsync` with a minimal `ClaimsPrincipal`
  (no personal data). The cookie only proves "passed the gate"; it is never used for
  reset authorization.
- ASP.NET Core 10 already returns **401/403 instead of a login redirect** for API
  endpoints (endpoints that use JSON or `TypedResults`)
  ([ASP.NET Core 10 notes](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)).
  The page JavaScript shows the access-code form on 401.
- The cookie is encrypted with **Data Protection** keys. On App Service the keys are
  stored automatically in `%HOME%/ASP.NET/DataProtection-Keys` and survive
  restarts, so visitors stay signed in after a restart
  ([key management](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0)).
  Deployment slots do not share keys; we do not use slots.
- **CSRF:** pages call the API with `fetch` and a JSON body. A cross-site HTML form
  cannot send `application/json`, and the cookie is `SameSite=Strict`. Avoid
  `[FromForm]` endpoints: since .NET 8 they require the antiforgery middleware.

#### HTTPS, forwarded headers and security headers

- App Service terminates TLS. Without help, the app sees plain HTTP, so
  `UseHsts` does nothing and redirects can loop. Set the app setting
  **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`**; ASP.NET Core then reads
  `X-Forwarded-Proto` and `X-Forwarded-For` (also giving the rate limiter the real
  client IP) ([proxy guidance](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)).
- Turn on App Service **HTTPS Only** (the platform redirects HTTP to HTTPS) and call
  `app.UseHsts()` outside Development.
- ASP.NET Core has no built-in CSP middleware. A few lines are enough:

```csharp
app.Use(async (context, next) =>
{
    IHeaderDictionary headers = context.Response.Headers;
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; " +
        "connect-src 'self'; object-src 'none'; base-uri 'none'; " +
        "form-action 'self'; frame-ancestors 'none'";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "microphone=(self), camera=(), geolocation=()";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    await next(context);
});
```

- The CSP forbids inline `<script>` and `<style>`: keep all JavaScript and CSS in
  files. The `AudioWorklet` module is a script file and is covered by
  `script-src 'self'`. `connect-src 'self'` allows same-origin `fetch` and, in
  current browsers, same-origin `wss:`; the console check in testing confirms it.
- `Referrer-Policy: no-referrer` matters for the reset form: a page opened from a
  reset link must never leak that URL to another site.
- Add `Cache-Control: no-store` to API responses and to the reset form page.

#### Static files

- The pages are plain HTML/JS/CSS in `wwwroot`. Use `app.MapStaticAssets()` (the
  .NET 9+ default; adds ETags and compression) or `app.UseStaticFiles()`; both are
  fine.
- Static files are served **without authorization**. That is OK: the voice page has
  no secrets in it. The access gate protects the API and the WebSocket, not the HTML.
- Include a `favicon.ico` (a missing favicon is a console error).

#### Errors: ProblemDetails

```csharp
builder.Services.AddProblemDetails();
app.UseExceptionHandler(); // unhandled exception -> 500 ProblemDetails, no stack trace
app.UseStatusCodePages();  // empty 4xx/5xx -> ProblemDetails body
```

- Never put exception messages, inputs, tokens or codes in a response. Error
  messages are fixed strings, like the mock contract requires.
- The mock apps return the contract's own error shape
  (`{"error":{"code":"...","message":"..."}}`), not ProblemDetails.

#### Health and version endpoint

- `GET /health` returns `{"status":"ok","commit":"<sha>"}`. Anonymous, no secrets,
  no dependency checks (it must stay cheap and say "the process is up").
- Point the App Service **Health check** setting at `/health`.
- The built-in health checks (`AddHealthChecks()` / `MapHealthChecks`) are only
  worth it if we add a readiness check for storage; for one endpoint, a small
  Minimal API handler is simpler. See 2.9 for the commit SHA.

#### TimeProvider, BackgroundService, PeriodicTimer, shutdown

- Register `builder.Services.AddSingleton(TimeProvider.System);` and inject
  `TimeProvider` wherever time matters (call limits, expiry, reconciliation).
  Never call `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly in logic.
- Timers and delays also take the provider: `new PeriodicTimer(interval, timeProvider)`,
  `Task.Delay(delay, timeProvider, ct)`, `new CancellationTokenSource(timeout, timeProvider)`.
  This makes call-duration limits testable with `FakeTimeProvider`.
- Reconciliation worker:

```csharp
internal sealed class ReconciliationWorker(TimeProvider timeProvider, ILogger<ReconciliationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        do
        {
            try
            {
                await ReconcileOpenSessionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.ReconciliationFailed(logger, ex); // log and try again next tick
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

- The first pass runs immediately at startup, then every minute. Catch exceptions
  inside the loop: an unhandled exception in a `BackgroundService` **stops the whole
  app** (default since .NET 6).
- .NET 10 change: all of `ExecuteAsync` now runs on a background thread, so it never
  delays startup ([breaking change](https://learn.microsoft.com/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task)).
  Sessions are independent, so new calls do not need to wait for reconciliation.
- **Graceful shutdown:** on stop, `ApplicationStopping` fires and the stopping
  token is cancelled. Close open voice sockets with status 1001 ("going away") so
  the page shows "Connection lost". Do not rely on shutdown time to save state: state
  is written to storage at every transition, and reconciliation fixes the rest. That
  is the real restart safety.

### 2.5 Logging and observability

- **Source-generated logging** with `[LoggerMessage]`. The recommended analyzer
  level already warns (CA1848, CA1873) on `logger.LogInformation(...)` calls. Put
  the messages of one area in one `static partial class`:

```csharp
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Session {SessionId} moved from {FromState} to {ToState}")]
    public static partial void StateChanged(
        ILogger logger, string sessionId, RecoveryState fromState, RecoveryState toState);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation pass failed")]
    public static partial void ReconciliationFailed(ILogger logger, Exception exception);
}
```

- **Structured logging:** named placeholders, never string interpolation in
  messages. Log IDs and state names, not content.
- **What we never log:** request or response bodies, the access code, verification
  codes, reset tokens or links, passwords, transcript text, raw model tool arguments
  (log the tool name and the validation outcome only), `Authorization` headers.
  Do not enable `AddHttpLogging` or W3C logging.
- The typed log methods are a whitelist: a parameter that is not in a log method
  signature cannot reach the logs. Code review checks every new log method.
- **Redaction library** (`Microsoft.Extensions.Compliance.Redaction`): it only
  redacts log parameters marked with a data-classification attribute. It does not
  find a password inside free text, so it would not protect transcripts. We skip it
  and rely on "never log it" plus our own transcript masking, and we document this
  limit honestly ([data redaction](https://learn.microsoft.com/dotnet/core/extensions/data-redaction)).
- **Application Insights:** use the **Azure Monitor OpenTelemetry distro**
  (`Azure.Monitor.OpenTelemetry.AspNetCore`), not the classic Application Insights
  SDK ([enable OpenTelemetry](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable)).

```csharp
builder.Services.AddOpenTelemetry().UseAzureMonitor(options => options.Credential = credential);
```

  - The connection string comes from the `APPLICATIONINSIGHTS_CONNECTION_STRING`
    app setting (it is not a secret). With `Credential` set, ingestion uses
    managed identity (role "Monitoring Metrics Publisher" on the App Insights resource).
  - Set `OTEL_SERVICE_NAME` (for example `voice-reset-agent`, `voice-reset-mocks`) so
    the two apps show as separate roles on the application map.
  - Since distro 1.5.0 the default sampling is **rate-limited, about 5 traces per
    second**. That is plenty for a demo; we keep the default
    ([changelog](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/CHANGELOG.md)).
  - The distro exports `ILogger` logs too, so the "never log secrets" rule covers
    Application Insights automatically.
  - OpenTelemetry's ASP.NET Core and HttpClient instrumentation **redact query
    string values** by default (`?token=Redacted`), but **not path segments**. So a
    token must never be in a URL path. Prefer the URL fragment (`#token=...`, never
    sent to the server) or the request body.
  - For business events (session started/ended, outcome) use log messages with
    clear names; no custom metrics are needed.

### 2.6 Azure SDK for .NET

#### Credentials

([authentication best practices](https://learn.microsoft.com/dotnet/azure/sdk/authentication/best-practices))

- Microsoft recommends a **deterministic credential in production**:
  `DefaultAzureCredential` tries several sources and can silently pick the wrong one.

```csharp
TokenCredential credential = builder.Environment.IsDevelopment()
    ? new AzureCliCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
```

- Create the credential **once** and share it (it caches tokens).
- Each app gets its **own system-assigned managed identity**. Give each identity
  only the roles it needs, scoped as narrowly as possible (a single table or blob
  container when the role allows it). The agent's identity has no access to the
  mocks' data; the mocks' identity has no access to transcripts.

#### Client lifetime and registration

([ASP.NET Core guidance](https://learn.microsoft.com/dotnet/azure/sdk/aspnetcore-guidance))

- Azure SDK clients are **thread-safe and meant to be singletons**. Register them
  with `Microsoft.Extensions.Azure`, which also forwards SDK logs to `ILogger`:

```csharp
builder.Services.AddAzureClients(clients =>
{
    clients.AddTableServiceClient(storageOptions.TableEndpoint);
    clients.AddBlobServiceClient(storageOptions.BlobEndpoint);
    clients.AddClient<VoiceLiveClient, VoiceLiveClientOptions>(
        (options, tokenCredential) => new VoiceLiveClient(voiceLiveEndpoint, tokenCredential, options));
    clients.UseCredential(credential);
});
```

- `VoiceLiveClient` (singleton) creates one `VoiceLiveSession` per call; dispose the
  session when the call ends. Voice Live needs the **Cognitive Services User** role.
- `CallAutomationClient` also accepts a `TokenCredential`; register it the same way,
  only when the phone channel is enabled.
- Use endpoints (URIs) plus managed identity, never storage account keys or
  connection strings.

#### Azure Table Storage (`Azure.Data.Tables`)

- **Keys:** `PartitionKey = sessionId`, `RowKey = "session"` for the session row.
  Other rows for the same session (for example events) can share the partition,
  which allows batch transactions inside one session. Reconciliation finds open
  sessions with a filter query on `State`; at our scale a scan is fine.
- **Optimistic concurrency with ETags:** read the entity, change it, then write
  with the ETag you read. If another writer changed it first, the service answers
  412 and you reload and decide again. This protects against duplicate events and
  two workers (call + reconciliation) racing.

```csharp
try
{
    await _table.UpdateEntityAsync(session, session.ETag, TableUpdateMode.Replace, cancellationToken);
    return true;
}
catch (RequestFailedException ex) when (ex.Status == 412)
{
    return false; // Someone else changed it first: reload and decide again.
}
```

- Use `TableUpdateMode.Replace` (the stored row is exactly our object; `Merge`
  can keep stale columns). Use `AddEntityAsync` to create (fails with 409 if the row
  already exists, which makes "create once" safe). Use `GetEntityIfExistsAsync` for
  reads that may miss.
- Entity class: implement `ITableEntity` with simple property types (string, int,
  bool, `DateTimeOffset`). Store the state enum **as a string** and parse it
  explicitly: since 12.12, the SDK silently skips enum values it does not know, so a
  renamed state would load as the default value without an error.
- Store time as UTC `DateTimeOffset` from `TimeProvider`.

#### Blob Storage (`Azure.Storage.Blobs`)

- One private container for transcripts; one blob per session
  (`transcripts/{sessionId}.json`), written once at the end of the session after
  masking. Use `UploadAsync(..., overwrite: false)` so a second writer fails instead
  of replacing the transcript.
- Retention by a **lifecycle management rule** on the storage account (infrastructure,
  not code). Public access disabled on the account.

#### Key Vault: App Service references, not the configuration provider

- Use **App Service Key Vault references** in app settings:
  `@Microsoft.KeyVault(VaultName=<vault>;SecretName=<name>)`. No code and no extra
  package; the app reads a normal setting. The app's identity needs the
  **Key Vault Secrets User** role.
- Values are cached and refreshed within 24 hours; any settings change restarts the
  app and fetches them again. Good enough for an access code.
- The configuration provider package (`Azure.Extensions.AspNetCore.Configuration.Secrets`)
  adds code and startup dependencies for no gain here.

### 2.7 Testing

#### Framework: xUnit v3

- **xUnit v3 is the current line** (version 4.0.1, released August 2026). v2 is in
  maintenance mode (critical fixes only). Use v3
  ([xunit.net releases](https://xunit.net/releases/)).
- v3 test projects are executables (`OutputType=Exe`) and run on **Microsoft Testing
  Platform (MTP) v2** by default from 4.0. With the .NET 10 SDK, add this to
  `global.json` so `dotnet test` uses MTP
  ([xUnit MTP docs](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)):

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

- The package `xunit.v3` is enough (it brings `xunit.v3.mtp-v2`).
  `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` are only needed for old
  VSTest-based runners; add them back only if an IDE does not discover the tests.
- Pass `TestContext.Current.CancellationToken` to async calls in tests (the xUnit
  analyzer suggests it). It cancels hanging tests cleanly.
- Run tests with `dotnet test` from `solution/`.

#### In-process HTTP tests with `WebApplicationFactory`

([integration tests](https://learn.microsoft.com/aspnet/core/test/integration-tests?view=aspnetcore-10.0))

- `Microsoft.AspNetCore.Mvc.Testing` starts the real app in memory. In .NET 10 a
  source generator makes the top-level `Program` class public, so
  **`public partial class Program { }` is no longer needed**
  ([dotnet/aspnetcore#58199](https://github.com/dotnet/aspnetcore/pull/58199)).
- Replace I/O services in `ConfigureTestServices`: in-memory session and transcript
  stores, `FakeTimeProvider`, a fake Voice Live session.
- The agent's typed `HttpClient`s can talk to the **real Mocks app in memory**:
  create a `WebApplicationFactory` for the Mocks app and route the agent's client
  to it with `ConfigurePrimaryHttpMessageHandler(() => mocksFactory.Server.CreateHandler())`.
  This tests the real contract with no network.
- Both apps have a class named `Program` in the global namespace. Keep one test
  project per app, or use any other public type of the app as the factory's type
  argument (`WebApplicationFactory<TEntryPoint>` only needs *a type in that
  assembly*).
- **Restart test:** dispose the factory in the middle of a reset, create a new one
  on the same store, and assert the outcome is truthful.

#### Time: `FakeTimeProvider`

([FakeTimeProvider](https://learn.microsoft.com/dotnet/core/extensions/timeprovider-testing))

```csharp
var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
time.Advance(TimeSpan.FromMinutes(11)); // timers and delays due in this window fire now
```

- Timers and `Task.Delay` only fire when the test calls `Advance`; tests never sleep.
- Never mix real time and fake time in one test.

#### Fakes, not a mocking library

- Write small **hand-written fakes** for our few interfaces (`InMemorySessionStore`,
  `FakeVoiceModelSession`, `RecordingTranscriptStore`). They are plain C#, easy to
  read, and reusable across tests.
- For HTTP, use the real Mocks app in memory (above) or a tiny fake
  `HttpMessageHandler`.
- **No Moq:** Moq 4.20.0 (August 2023) shipped "SponsorLink", which read the
  developer's git email and sent a hash of it to a server; it was removed after a
  public backlash ([news](https://www.bleepingcomputer.com/tag/moq/)), but trust was
  damaged. NSubstitute (6.2.0) is the usual alternative if we ever need one.
- **No FluentAssertions:** since version 8 it needs a paid licence for commercial
  use ([devclass](https://devclass.com/2025/01/16/another-open-source-project-shifts-to-restrictive-license-fluent-assertions-following-xceed-partnership)).
  xUnit's `Assert` is enough.

#### Test style

([unit testing best practices](https://learn.microsoft.com/dotnet/core/testing/unit-testing-best-practices))

- Name: `MethodName_Scenario_ExpectedBehavior`, for example
  `VerifyCode_WrongCodeThreeTimes_LocksRecovery`.
- **Arrange / Act / Assert** blocks, one Act per test. Use `[Theory]` with
  `[InlineData]` instead of loops in tests.
- No logic (`if`, loops) in tests. No shared mutable state between tests.
- **Test data builders**: small static helper methods (`Sessions.InState(RecoveryState.Verified)`)
  instead of a builder framework.
- **Deterministic:** no real time (`FakeTimeProvider`), no network (in-memory apps
  and fakes), no random values without a fixed seed, no dependence on test order.
- Every guardrail scenario in `04-guardrails-plan.md` gets a test named after the
  scenario.

### 2.8 Clean code, SOLID and patterns, the pragmatic way

**When is an interface justified?** Only when it makes testing or reading easier
*today*:

- Yes, at **I/O boundaries** that tests must replace: session store (Table Storage),
  transcript store (Blob Storage), the Voice Live session (so the session logic can
  be tested without Azure), the phone/browser audio channel (two real
  implementations: browser WebSocket and ACS).
- No for pure logic (state machine, masking, argument validation): test the real
  class.
- No for things the framework already abstracts: `TimeProvider` (not `IClock`),
  `ILogger<T>`, `IOptions<T>`, typed `HttpClient` (test with a handler).
- No "one interface per class" habit, no generic `IRepository<T>`.

**SOLID in practice here:**

- *Single responsibility:* one class per concept (session state machine, tool
  dispatcher, transcript masker, issuer client), each small enough to explain in a
  minute.
- *Open/closed and Liskov:* the audio channel abstraction is the one real use; the
  shared session must not know which channel it talks to (a `CLAUDE.md` rule).
- *Dependency inversion:* only at the boundaries listed above.

**State machine: a `switch` expression over an enum.** It is the simplest readable
option, needs no package, and the whole transition table fits on one screen (it also
maps 1:1 to a Mermaid state diagram and to a `[Theory]` test table). A library such
as Stateless (5.20.1) adds a DSL the owner would have to explain, for no real gain.

```csharp
public static RecoveryState? Next(RecoveryState current, RecoveryEvent recoveryEvent) =>
    (current, recoveryEvent) switch
    {
        (RecoveryState.Started, RecoveryEvent.CodeSent) => RecoveryState.AwaitingCode,
        (RecoveryState.AwaitingCode, RecoveryEvent.CodeAccepted) => RecoveryState.Verified,
        (RecoveryState.Verified, RecoveryEvent.LinkSent) => RecoveryState.LinkSent,
        (RecoveryState.LinkSent, RecoveryEvent.ResetConfirmed) => RecoveryState.Completed,
        (not RecoveryState.Completed, RecoveryEvent.Escalate) => RecoveryState.Escalated,
        (not RecoveryState.Completed, RecoveryEvent.Cancel) => RecoveryState.Cancelled,
        _ => null, // Not allowed: the caller reports a refusal and the state does not change.
    };
```

(The states above are an example; the real list comes from the architecture step.)
The session class calls this, checks the result, saves with the ETag, and only then
tells the model what happened. The model never changes state directly.

**Result values vs exceptions:**

- **Expected outcomes** (wrong code, policy violation, issuer rejected, outcome
  unknown after a timeout, tool not allowed in this state) are **return values**.
  C# 14 has no built-in union type, so use a small closed `record` hierarchy and a
  `switch`:

```csharp
public abstract record IssuerResult
{
    private IssuerResult() { } // only the nested types below can inherit

    public sealed record Success(string OperationId) : IssuerResult;
    public sealed record Rejected(string ErrorCode) : IssuerResult;
    public sealed record Unknown : IssuerResult; // timeout: reconcile later
}
```

- **Exceptions** are for bugs and infrastructure faults (storage down, invalid
  configuration). Catch them at boundaries (endpoint, WebSocket loop, worker loop),
  log once, and turn them into a safe generic answer.
- Catch specific exceptions (`RequestFailedException` with a status code,
  `HttpRequestException`, `TimeoutRejectedException`), not `Exception`, except at the
  outermost loop boundary.
- No Result/OneOf NuGet library: the 10-line record hierarchy is clearer.

### 2.9 Versioning and deployment

#### Commit SHA in the build

- Since .NET 8, Source Link is part of the SDK and the build appends the git commit
  to `AssemblyInformationalVersion`, for example `1.0.0+3f2c...`
  ([breaking change note](https://learn.microsoft.com/dotnet/core/compatibility/sdk/8.0/source-link)).
- Automatic detection needs the `.git` data at build time and does not notice
  uncommitted changes. To be explicit and reproducible, **pass the SHA in the publish
  command** and build from a clean checkout of the pinned commit:

```bash
dotnet publish src/VoiceReset.Agent -c Release -o out/agent -p:SourceRevisionId=$(git rev-parse HEAD)
```

- Read it at runtime and show it on `/health`:

```csharp
string version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
string commit = version.Contains('+') ? version[(version.IndexOf('+') + 1)..] : "unknown";
```

#### Deploy to App Service Linux

([deploy ZIP](https://learn.microsoft.com/azure/app-service/deploy-zip),
[configure ASP.NET Core on App Service](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore?pivots=platform-linux))

```bash
# Build and zip the publish output (the zip must contain the files, not a parent folder)
dotnet publish src/VoiceReset.Agent -c Release -o out/agent -p:SourceRevisionId=$(git rev-parse HEAD)
(cd out/agent && zip -r ../agent.zip .)        # PowerShell: Compress-Archive -Path out/agent/* -DestinationPath out/agent.zip

# One-time app configuration
az webapp config set -g <rg> -n <app> --linux-fx-version "DOTNETCORE|10.0" --always-on true \
  --startup-file "dotnet VoiceReset.Agent.dll"
az webapp update -g <rg> -n <app> --https-only true
az webapp config appsettings set -g <rg> -n <app> --settings ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# Deploy (restarts the app)
az webapp deploy -g <rg> -n <app> --src-path out/agent.zip --type zip
```

- `az webapp deploy` does **not** build on the server by default: we upload compiled
  output, so the deployed binary is exactly the one we built from the pinned commit.
- **WebSockets:** always enabled on Linux App Service; the `webSocketsEnabled`
  setting does not apply to Linux ([Linux FAQ](https://learn.microsoft.com/troubleshoot/azure/app-service/faqs-app-service-linux-new)).
- **Always On** keeps the app loaded (no cold start, background worker keeps
  running). It needs the Basic tier or higher.
- An explicit `--startup-file` avoids guessing when the zip contains more than one
  `*.runtimeconfig.json`.
- Use Microsoft Entra login for `az` (no basic-auth publishing credentials).
- After deploy, check `/health` shows the expected commit (the setup guide step 8).

---

## 3. What we deliberately skip

These are good tools in other projects but would be over-engineering here. Each
skip is a choice we can explain.

| Skipped | Why |
| --- | --- |
| Clean Architecture project layers (Domain / Application / Infrastructure projects) | Two small apps. Folders inside one project give the same separation without project plumbing. |
| A shared contracts project between Agent and Mocks | The agent is a client of a third-party API. Separate DTOs on each side catch contract mismatches instead of hiding them. |
| MediatR, CQRS, AutoMapper | Indirection with no benefit at this size. Call the method; map by hand. |
| Generic repository / unit of work | The Table SDK is already the data API; one small store class per table. |
| Stateless (state machine library) | A `switch` expression is shorter and easier to explain and test. |
| FluentValidation | Data annotations for options; explicit checks for tool arguments. |
| Result/OneOf libraries | A closed `record` hierarchy is 10 lines. |
| Custom Polly pipelines, hedging | The standard resilience handler with retries off for POST covers it. |
| Redaction library and data-classification taxonomy | It cannot mask free text; "never log it" plus transcript masking is the real control. |
| Key Vault configuration provider | App Service Key Vault references need no code. |
| `DefaultAzureCredential` in production | Not deterministic; Microsoft advises a specific credential. |
| Moq, FluentAssertions 8, AutoFixture | Trust and licence issues; hand-written fakes and `Assert` are clearer. |
| Testcontainers / Azurite in the test suite | In-memory fakes keep tests fast and deterministic. Real storage is checked in the Azure end-to-end runs. |
| Controllers, MVC, Razor | Minimal APIs and static HTML are enough. |
| OpenAPI/Swagger generation, API versioning | The mock contract is already the spec; no external API consumers. |
| .NET Aspire, Docker, containers | One App Service plan with zip deploy is simpler and matches the plan. |
| Native AOT, trimming, `InvariantGlobalization` | No startup or size problem to solve. |
| Third-party analyzers (StyleCop, Roslynator, Sonar) | The SDK analyzers at `latest-recommended` are enough. |
| Code coverage targets, mutation testing | We test every rule and guardrail scenario by name instead. |
| Distributed rate limiting / Redis | One instance; documented as a limitation. |
| Deployment slots | They do not share Data Protection keys and add cost; a second web app is used for the restart test anyway. |

---

## 4. Ready-to-use files

All four files go in `solution/`. They were tested together: a web project with all
the packages below and an xUnit v3 test project built with 0 warnings in Release,
and `dotnet test` passed on Microsoft Testing Platform.

### 4.1 `global.json`

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

(`latestFeature` accepts the installed 10.0.302 and any newer .NET 10 SDK such as
10.0.401.)

### 4.2 `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors Condition="'$(Configuration)' == 'Release'">true</TreatWarningsAsErrors>
  </PropertyGroup>
  <PropertyGroup Condition="'$(GITHUB_ACTIONS)' == 'true'">
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

Project files then stay tiny, for example the agent:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Azure.AI.VoiceLive" />
    <PackageReference Include="Azure.Communication.CallAutomation" />
    <PackageReference Include="Azure.Data.Tables" />
    <PackageReference Include="Azure.Storage.Blobs" />
    <PackageReference Include="Azure.Identity" />
    <PackageReference Include="Microsoft.Extensions.Azure" />
    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
  </ItemGroup>
</Project>
```

and a test project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\VoiceReset.Agent\VoiceReset.Agent.csproj" />
  </ItemGroup>
</Project>
```

### 4.3 `Directory.Packages.props` (versions checked on nuget.org, 2026-10-03)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Azure -->
    <PackageVersion Include="Azure.AI.VoiceLive" Version="1.2.0" />
    <PackageVersion Include="Azure.Communication.CallAutomation" Version="1.6.1" />
    <PackageVersion Include="Azure.Data.Tables" Version="12.13.0" />
    <PackageVersion Include="Azure.Storage.Blobs" Version="12.30.0" />
    <PackageVersion Include="Azure.Identity" Version="1.21.0" />
    <PackageVersion Include="Microsoft.Extensions.Azure" Version="1.14.1" />
    <PackageVersion Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.6.0" />
    <!-- ASP.NET Core / extensions -->
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.10.0" />
    <!-- Tests -->
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
  </ItemGroup>
</Project>
```

| Package | Version | Notes |
| --- | --- | --- |
| Azure.AI.VoiceLive | 1.2.0 | GA, 2026-08-07. Has a `net10.0` build. |
| Azure.Communication.CallAutomation | 1.6.1 | Accepts `TokenCredential` (managed identity). |
| Azure.Data.Tables | 12.13.0 | 2026-09-23. |
| Azure.Storage.Blobs | 12.30.0 | |
| Azure.Identity | 1.21.0 | |
| Microsoft.Extensions.Azure | 1.14.1 | `AddAzureClients`. |
| Azure.Monitor.OpenTelemetry.AspNetCore | 1.6.0 | 2026-07-27. Default sampling 5 traces/s. |
| Microsoft.Extensions.Http.Resilience | 10.10.0 | Standard resilience handler. |
| Microsoft.Extensions.TimeProvider.Testing | 10.10.0 | `FakeTimeProvider`. |
| xunit.v3 | 4.0.1 | Brings `xunit.v3.mtp-v2` (MTP v2). |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | Matches the latest .NET 10 patch. |
| *Optional:* xunit.runner.visualstudio | 4.0.0 | Only if an IDE needs VSTest discovery. |
| *Optional:* Microsoft.NET.Test.Sdk | 18.10.1 | Same. |

Not used (see section 3): Microsoft.Extensions.Compliance.Redaction 10.10.0,
Azure.Extensions.AspNetCore.Configuration.Secrets 1.5.2, Stateless 5.20.1,
Moq 4.21.0, FluentAssertions 8.11.0.

### 4.4 `.editorconfig` (concise)

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4

[*.{json,xml,csproj,props,targets,slnx,yml,yaml,js,html,css}]
indent_size = 2

[*.cs]
# Namespaces and usings
csharp_style_namespace_declarations = file_scoped:warning
csharp_using_directive_placement = outside_namespace:warning
dotnet_sort_system_directives_first = true

# Braces and layout (Allman)
csharp_new_line_before_open_brace = all
csharp_prefer_braces = true:warning

# var: only when the type is obvious
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion

# Modern, readable C#
dotnet_style_prefer_collection_expression = when_types_loosely_match:suggestion
csharp_style_prefer_primary_constructors = true:suggestion
csharp_style_prefer_switch_expression = true:suggestion
csharp_style_prefer_pattern_matching = true:suggestion
dotnet_style_readonly_field = true:warning
dotnet_style_require_accessibility_modifiers = for_non_interface_members:warning
dotnet_code_quality_unused_parameters = all:warning

# Naming: PascalCase constants, s_camelCase private static fields, _camelCase private fields
dotnet_naming_rule.constants_pascal.symbols = constants
dotnet_naming_rule.constants_pascal.style = pascal_case
dotnet_naming_rule.constants_pascal.severity = warning
dotnet_naming_symbols.constants.applicable_kinds = field, local
dotnet_naming_symbols.constants.required_modifiers = const

dotnet_naming_rule.private_static_fields.symbols = private_static_fields
dotnet_naming_rule.private_static_fields.style = s_camel_case
dotnet_naming_rule.private_static_fields.severity = warning
dotnet_naming_symbols.private_static_fields.applicable_kinds = field
dotnet_naming_symbols.private_static_fields.applicable_accessibilities = private, internal
dotnet_naming_symbols.private_static_fields.required_modifiers = static

dotnet_naming_rule.private_fields.symbols = private_fields
dotnet_naming_rule.private_fields.style = underscore_camel_case
dotnet_naming_rule.private_fields.severity = warning
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private, internal

dotnet_naming_style.pascal_case.capitalization = pascal_case
dotnet_naming_style.s_camel_case.capitalization = camel_case
dotnet_naming_style.s_camel_case.required_prefix = s_
dotnet_naming_style.underscore_camel_case.capitalization = camel_case
dotnet_naming_style.underscore_camel_case.required_prefix = _

# Tests: Method_Scenario_Expected names use underscores
[tests/**.cs]
dotnet_diagnostic.CA1707.severity = none
```

Note: we do not enforce IDE0005 (unused `using`) on build, because it only works
when `GenerateDocumentationFile` is on, which would require XML comments on every
public member. The IDE still shows unused usings.

---

## 5. Proposed CLAUDE.md rule updates

These bullets replace the placeholder line in rule 1 ("The concrete best practices
for the stack are pending research..."). 24 bullets.

- Target .NET 10 LTS (`net10.0`) and C# 14 defaults. Use `global.json`, one
  `.slnx`, `Directory.Build.props` and Central Package Management (`Directory.Packages.props`).
- Nullable reference types on; treat nullable warnings as bugs; avoid `!` and explain any use.
- `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild`; Release builds
  treat warnings as errors. Fix warnings; suppress only with a one-line reason.
- Follow the Microsoft C# conventions: file-scoped namespaces, `_camelCase` private
  fields, `Async` suffix, `var` only when the type is obvious, classes `sealed` by default.
- Use records for immutable data, primary constructors for simple dependency
  holders, `required`/`init` for options and entities, collection expressions.
- Minimal APIs with route groups and `TypedResults`; handlers are named methods in
  small static endpoint classes; `Program.cs` only wires things up.
- Every options class is bound with `ValidateDataAnnotations().ValidateOnStart()`;
  never read `IConfiguration` directly in business code.
- JSON is strict everywhere: unknown members and duplicate properties are rejected,
  nullable annotations respected, `snake_case` for the contract APIs.
- One typed `HttpClient` per external API with the standard resilience handler and
  `DisableForUnsafeHttpMethods()`; an ambiguous POST outcome is "unknown" and goes
  to reconciliation, never a blind retry.
- Inject `TimeProvider` for all time, delays and timers; never use `DateTime.Now`/`UtcNow` in logic.
- Always pass and honour `CancellationToken`; no `.Result`/`.Wait()`; one sender at a
  time per WebSocket; WebSocket loops treat disconnects as dropped calls, not errors.
- Interfaces only at I/O boundaries that tests replace (stores, Voice Live session,
  audio channel); pure logic is tested through its real class.
- The recovery state machine is an enum plus a `switch` expression; no state-machine library.
- Expected failures are return values (small closed record hierarchies); exceptions
  are for bugs and infrastructure faults, caught at the endpoint/loop boundary.
- Logging uses source-generated `[LoggerMessage]` methods with structured
  placeholders; log IDs and states, never bodies, codes, tokens, passwords,
  transcript text or raw tool arguments.
- Tokens never go in URL paths (telemetry records paths); use the body or the URL fragment.
- Azure: `ManagedIdentityCredential` in Azure, `AzureCliCredential` locally, never
  `DefaultAzureCredential` in production; one shared credential; Azure clients are
  singletons registered with `AddAzureClients`; no keys or connection strings.
- Table Storage writes use ETags (`UpdateEntityAsync` with the read ETag,
  `TableUpdateMode.Replace`); store enums as strings; times are UTC `DateTimeOffset`.
- Secrets reach the app only as App Service Key Vault references; startup
  validation rejects unresolved `@Microsoft.KeyVault(` values.
- Security headers (CSP without inline script/style, `nosniff`, `no-referrer`,
  `Permissions-Policy`), HSTS, forwarded headers on, HTTPS only; the access-code
  cookie is `__Host-`, `HttpOnly`, `Secure`, `SameSite=Strict`, compared in constant time.
- Tests: xUnit v3 on Microsoft Testing Platform, `WebApplicationFactory` for HTTP,
  `FakeTimeProvider` for time, hand-written fakes (no Moq, no FluentAssertions).
- Test names are `Method_Scenario_Expected`, written as Arrange/Act/Assert with one
  Act; tests are deterministic (no real time, network, randomness or ordering).
- Every guardrail scenario and every state transition has a named test.
- The build stamps the commit SHA (`-p:SourceRevisionId=$(git rev-parse HEAD)`) and
  `/health` shows it; deploy the Release `dotnet publish` output as a zip with
  `az webapp deploy`.

---

## Sources

- .NET release metadata: [releases-index.json](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json), [10.0 releases](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
- [What's new in C# 14](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-14)
- [What's new in .NET 10 libraries](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries) (JSON `Strict`, `AllowDuplicateProperties`, `WebSocketStream`)
- [What's new in ASP.NET Core 10](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0)
- [`dotnet new sln` creates .slnx in .NET 10](https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default)
- [Code analysis overview](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview), [MSBuild properties](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props)
- [C# coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions), [Identifier names](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names)
- [APIs overview (Minimal APIs recommended)](https://learn.microsoft.com/aspnet/core/fundamentals/apis?view=aspnetcore-10.0)
- [Options pattern](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0)
- [HTTP resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)
- [WebSockets in ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0)
- [Rate limiting middleware](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Data Protection key management](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0)
- [Proxy and load balancer configuration](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [BackgroundService runs all of ExecuteAsync as a Task (.NET 10)](https://learn.microsoft.com/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task)
- [Data redaction in .NET](https://learn.microsoft.com/dotnet/core/extensions/data-redaction)
- [Enable OpenTelemetry in Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable), [configuration](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-configuration), [distro changelog](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/monitor/Azure.Monitor.OpenTelemetry.AspNetCore/CHANGELOG.md)
- [OpenTelemetry ASP.NET Core instrumentation changelog](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.AspNetCore/CHANGELOG.md) (query redaction)
- [Azure Identity best practices](https://learn.microsoft.com/dotnet/azure/sdk/authentication/best-practices), [Azure SDK with ASP.NET Core](https://learn.microsoft.com/dotnet/azure/sdk/aspnetcore-guidance)
- [Azure.Data.Tables changelog](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/tables/Azure.Data.Tables/CHANGELOG.md)
- [Azure.AI.VoiceLive README](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.VoiceLive_1.2.0/sdk/voicelive/Azure.AI.VoiceLive/README.md)
- [App Service Key Vault references](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
- [xUnit.net releases](https://xunit.net/releases/), [v3 4.0.0 notes](https://xunit.net/releases/v3/4.0.0), [xUnit and Microsoft Testing Platform](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [Integration tests in ASP.NET Core](https://learn.microsoft.com/aspnet/core/test/integration-tests?view=aspnetcore-10.0), [public Program source generator PR](https://github.com/dotnet/aspnetcore/pull/58199)
- [Testing with FakeTimeProvider](https://learn.microsoft.com/dotnet/core/extensions/timeprovider-testing)
- [Unit testing best practices](https://learn.microsoft.com/dotnet/core/testing/unit-testing-best-practices)
- Moq SponsorLink: [BleepingComputer coverage](https://www.bleepingcomputer.com/tag/moq/); FluentAssertions licence: [devclass](https://devclass.com/2025/01/16/another-open-source-project-shifts-to-restrictive-license-fluent-assertions-following-xceed-partnership)
- [Source Link included in the .NET SDK](https://learn.microsoft.com/dotnet/core/compatibility/sdk/8.0/source-link)
- [Deploy files to App Service](https://learn.microsoft.com/azure/app-service/deploy-zip), [Configure ASP.NET Core on App Service (Linux)](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore?pivots=platform-linux), [App Service on Linux FAQ](https://learn.microsoft.com/troubleshoot/azure/app-service/faqs-app-service-linux-new)
