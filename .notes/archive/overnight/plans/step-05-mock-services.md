# Step 5: Mock Services (`VoiceReset.Mocks`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the app-mocks web app: the mock issuer API, the mock ticket API and the mock recovery inbox, exactly as [docs/mock-contract.md](../../../docs/mock-contract.md) describes, with tests for every contract rule.

**Architecture:** One ASP.NET Core project (`src/VoiceReset.Mocks`), Minimal APIs for `/v1/...` and Razor Pages for `/inbox`. Plain service classes (one per feature) return an `ApiResult` (status code + JSON). State lives behind a small store interface (`IMockStore`) with two implementations: Azure Table Storage (ETag optimistic concurrency) and in-memory (tests, and when no endpoint is configured). Time always comes from `TimeProvider`, so tests move the clock with `FakeTimeProvider`.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs + Razor Pages, `System.Text.Json` (strict), `Azure.Data.Tables`, `Azure.Identity`, `Microsoft.Extensions.Azure`, xUnit v3 on Microsoft Testing Platform, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`.

---

## How to read this plan

- All commands run from the `solution/` folder in PowerShell.
- Every task follows the same rhythm: write a failing test, run it, write the code, run it again, commit.
- Commit messages are plain (`feat(mocks): ...`). **No** `Co-Authored-By` lines, **no** "Generated with" footers (CLAUDE.md rule 2).
- Names (folders, config keys, resource names) come from [00-overview.md](00-overview.md). Coding rules come from [research/dotnet-best-practices.md](../research/dotnet-best-practices.md). Where this plan needed something new, it is listed in "Additions to 00-overview" at the end.
- Test command form (xUnit v3 on Microsoft Testing Platform):
  `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "<Namespace.ClassName>"`.
  A passing run ends with a summary like `Test run summary: Passed!` and `failed: 0`. A failing run shows `failed: 1` (or more), or a build error `error CS0246`/`CS0103` when a type does not exist yet.
- Test names follow `Method_Scenario_Expected`, with Arrange/Act/Assert and no loops or `if` in test bodies (helpers may have logic).

## Rules the code must follow

1. **Never log** request bodies, verification codes, reset tokens, links or passwords. Logs use the source-generated `MockLog` methods, which only take IDs, outcomes and status codes.
2. **Errors** are always `{"error":{"code":"...","message":"..."}}` with a fixed message. Verification errors add `attempts_remaining` and `status`; policy errors add `policy_version` and `violations`. Nothing echoes the input.
3. **Strict JSON** (.NET 10 strict settings + `snake_case`): unknown fields, duplicate fields, wrong types, missing fields, `null` for a non-null field, and a non-JSON content type all give `400 invalid_request`. Every listed field must be present (the nullable ticket `reset_receipt` must be sent as `null`).
4. **Secrets are compared in constant time** (`CryptographicOperations.FixedTimeEquals` on SHA-256 hashes).
5. **The service credential never sees** codes, tokens, links, passwords or inbox contents. No response contains them.
6. **Azure credential is chosen in one place** (`Program.cs`): `AzureCliCredential` in Development, `ManagedIdentityCredential` (system-assigned) everywhere else. Never `DefaultAzureCredential`.
7. **Start-up validation** rejects missing values and unresolved Key Vault references (values starting with `@Microsoft.KeyVault(`).
8. Classes are `sealed`; records for request data; primary constructors for services; `TimeProvider` for every clock read.
9. **Store calls take no `CancellationToken` on purpose:** a mutation that has started is finished even if the client disconnects, so a retry always finds a consistent record. Reading the request body does use `RequestAborted`.

## File structure

Files created in `solution/src/VoiceReset.Mocks/`:

| File | Responsibility |
|---|---|
| `Program.cs` | Wiring only: credential, services, middleware, endpoint mapping |
| `appsettings.json` | Configuration shape with empty values (real values come from app settings / Key Vault references) |
| `Configuration/MocksOptions.cs` | `MocksOptions`, `MockUserOptions` (section `Mocks`) |
| `Configuration/FaultsOptions.cs` | `FaultsOptions` (section `Faults`) |
| `Configuration/MocksOptionsValidator.cs` | Start-up validation of `MocksOptions` |
| `Configuration/SyntheticUsers.cs` | Username normalisation, user lookup, inbox password check |
| `Shared/Secrets.cs` | Random IDs, codes, tokens; SHA-256; constant-time compare; password hashing |
| `Shared/MockJson.cs` | Strict JSON options and request body reader |
| `Shared/ApiResult.cs` | Status code + JSON body as an `IResult`; all error shapes |
| `Shared/RequestRules.cs` | Shared field checks (ID length, username length) |
| `Shared/MockLog.cs` | Source-generated log messages (no secrets) |
| `Shared/ServiceAuth.cs` | `ServiceAuth` (Bearer check) and `ServiceAuthFilter` (endpoint filter) |
| `Shared/SecurityHeaders.cs` | Security response headers middleware |
| `Shared/RateLimits.cs` | Rate limit policies (`api`, `inbox-login`) and the 429 response |
| `Shared/HealthEndpoints.cs` | `GET /health` with the commit SHA |
| `Storage/StoredRecord.cs` | Base class: `ETag` + `GetKey()` |
| `Storage/AccountRecord.cs` | Per normalised username: active recovery, last code time, password hash |
| `Storage/RecoveryRecord.cs` | One recovery: code hash, attempts, link, reset, receipt; `StoredResponse` |
| `Storage/IdempotencyRecord.cs` | Recorded results for `request_id` / `operation_id` keys |
| `Storage/TokenRecord.cs` | Token hash → recovery ID lookup |
| `Storage/TicketRecord.cs` | Ticket + outcome history (`TicketHistoryEntry`) |
| `Storage/InboxMessage.cs` | Inbox message + `InboxMessageKinds` |
| `Storage/IMockStore.cs` | Store interface + `StoreRetry` |
| `Storage/StoreTables.cs` | Table name per record type |
| `Storage/InMemoryMockStore.cs` | In-memory store (tests, no endpoint configured) |
| `Storage/TableMockStore.cs` | Azure Table Storage store |
| `Issuer/RecoveryStatus.cs` | Status and unlock status constants |
| `Issuer/IssuerRequests.cs` | Request records for the issuer routes |
| `Issuer/RecoveryService.cs` | Start recovery, read recovery status |
| `Issuer/VerificationService.cs` | Verify a code |
| `Issuer/ResetLinkService.cs` | Issue the reset link |
| `Issuer/PasswordPolicy.cs` | `PolicyRule`, `PasswordPolicy` |
| `Issuer/ResetService.cs` | Validate password, reset, reset operation status |
| `Issuer/ResetCompletion.cs` | Loads a recovery and completes a due pending reset |
| `Issuer/IssuerEndpoints.cs` | `/v1` issuer routes (named handler methods) |
| `Tickets/TicketOutcomes.cs` | Outcome and reason constants |
| `Tickets/TicketRequests.cs` | Request records for the ticket routes |
| `Tickets/TicketService.cs` | Create ticket, record outcome |
| `Tickets/TicketEndpoints.cs` | `/v1/tickets` routes |
| `Inbox/InboxAuth.cs` | Cookie authentication + antiforgery setup for the inbox |
| `Pages/_ViewImports.cshtml`, `Pages/_ViewStart.cshtml`, `Pages/Shared/_Layout.cshtml` | Razor Pages plumbing |
| `Pages/Inbox/Login.cshtml(.cs)` | `/inbox/login` |
| `Pages/Inbox/Index.cshtml(.cs)` | `/inbox` (message list, logout) |
| `wwwroot/inbox.css` | Inbox styles (no inline styles, CSP-friendly) |

Files created in `solution/tests/VoiceReset.Mocks.Tests/`:

| File | Responsibility |
|---|---|
| `MocksFactory.cs` | `WebApplicationFactory<Program>` with test config, `FakeTimeProvider`, shared store, log capture |
| `CapturingLoggerProvider.cs` | Hand-written logger that keeps every log line in memory |
| `TestApi.cs` | HTTP/JSON helpers |
| `TestJourney.cs` | Common flows (start, verify, link, reset) |
| `ThrowingStore.cs` | Store fake that always fails (for the 503 test) |
| `Shared/*Tests.cs` | Secrets, JSON, ApiResult, health, browser boundary |
| `Configuration/OptionsValidationTests.cs` | Options validation |
| `Storage/*` | Store contract tests (in-memory always, Table Storage with Azurite on demand) |
| `Issuer/*Tests.cs` | One file per issuer route |
| `Tickets/*Tests.cs` | Ticket create + outcome |
| `Inbox/*` | Inbox login + page, `InboxClient` helper |
| `LoggingTests.cs` | No secret appears in any log line |

Docs: `solution/docs/architecture/mock-services.md` (new), `solution/docs/submission/requirements-checklist.md` (section H).

---

### Task 0: Check the skeleton from step 4

**Files:**
- Check/Modify: `solution/global.json`
- Check/Modify: `solution/Directory.Packages.props`
- Check/Modify: `solution/src/VoiceReset.Mocks/VoiceReset.Mocks.csproj`
- Check/Modify: `solution/tests/VoiceReset.Mocks.Tests/VoiceReset.Mocks.Tests.csproj`

- [ ] **Step 1: Make sure `global.json` selects Microsoft Testing Platform**

`solution/global.json` must contain:

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

If the file exists with a different `sdk` block, keep that block and only make sure the `test` block is there.

- [ ] **Step 2: Add the package versions**

In `solution/Directory.Packages.props`, inside the `<ItemGroup>` of `<PackageVersion>` items, make sure these entries exist (add the missing ones, do not duplicate):

```xml
    <PackageVersion Include="Azure.Data.Tables" Version="12.13.0" />
    <PackageVersion Include="Azure.Identity" Version="1.21.0" />
    <PackageVersion Include="Microsoft.Extensions.Azure" Version="1.14.1" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.1" />
```

(Versions from the research, checked on nuget.org on 2026-10-03.)

- [ ] **Step 3: Set the app project file**

`solution/src/VoiceReset.Mocks/VoiceReset.Mocks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <RootNamespace>VoiceReset.Mocks</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Azure.Data.Tables" />
    <PackageReference Include="Azure.Identity" />
    <PackageReference Include="Microsoft.Extensions.Azure" />
  </ItemGroup>

</Project>
```

(`TargetFramework`, `Nullable`, `ImplicitUsings`, `AnalysisLevel` and `TreatWarningsAsErrors` come from `Directory.Build.props`.)

- [ ] **Step 4: Set the test project file**

`solution/tests/VoiceReset.Mocks.Tests/VoiceReset.Mocks.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <RootNamespace>VoiceReset.Mocks.Tests</RootNamespace>
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
    <ProjectReference Include="..\..\src\VoiceReset.Mocks\VoiceReset.Mocks.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Remove template leftovers**

Delete any template files step 4 may have left: `WeatherForecast*`, `Pages/Index.cshtml`, `Pages/Privacy.cshtml`, `Pages/Error.cshtml` (and their `.cs`), `wwwroot/lib/`, `tests/VoiceReset.Mocks.Tests/UnitTest1.cs`. Make `src/VoiceReset.Mocks/Program.cs` this minimal placeholder so the solution builds (Task 5 replaces it):

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();
await app.RunAsync();
```

- [ ] **Step 6: Build**

Run: `dotnet build VoiceReset.slnx -c Release`
Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 7: Commit (only if something changed)**

```powershell
git add global.json Directory.Packages.props src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests
git commit -m "chore(mocks): prepare mock services and test project"
```

---

### Task 1: Secrets and ID helpers

**Files:**
- Create: `src/VoiceReset.Mocks/Shared/Secrets.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Shared/SecretsTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/VoiceReset.Mocks.Tests/Shared/SecretsTests.cs`:

```csharp
using System.Buffers.Text;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Tests.Shared;

public sealed class SecretsTests
{
    [Theory]
    [InlineData(0, "000000")]
    [InlineData(47192, "047192")]
    [InlineData(999999, "999999")]
    public void FormatCode_Number_KeepsLeadingZeros(int number, string expected)
    {
        string code = Secrets.FormatCode(number);

        Assert.Equal(expected, code);
    }

    [Fact]
    public void NewCode_Called_ReturnsSixDigitString()
    {
        string code = Secrets.NewCode();

        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void NewToken_Called_ReturnsUrlSafe256BitValue()
    {
        string token = Secrets.NewToken();

        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.Equal(32, Base64Url.DecodeFromChars(token).Length);
    }

    [Fact]
    public void NewId_CalledTwice_ReturnsPrefixedUniqueIds()
    {
        string first = Secrets.NewId("rec");
        string second = Secrets.NewId("rec");

        Assert.StartsWith("rec_", first);
        Assert.Equal(28, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sha256Hex_KnownInput_ReturnsLowercaseHex()
    {
        string hash = Secrets.Sha256Hex("hello");

        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "abcd", false)]
    public void FixedTimeEquals_TwoStrings_ComparesContent(string left, string right, bool expected)
    {
        bool equal = Secrets.FixedTimeEquals(left, right);

        Assert.Equal(expected, equal);
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_IsSaltedAndVerifiesOnlyThatPassword()
    {
        string stored = Secrets.HashPassword("Correct-Horse-Battery-1");
        string storedAgain = Secrets.HashPassword("Correct-Horse-Battery-1");

        Assert.True(Secrets.VerifyPassword("Correct-Horse-Battery-1", stored));
        Assert.False(Secrets.VerifyPassword("Correct-Horse-Battery-2", stored));
        Assert.NotEqual(stored, storedAgain);
        Assert.DoesNotContain("Correct-Horse", stored);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Shared.SecretsTests"`
Expected: build error `CS0234`/`CS0246` (namespace `VoiceReset.Mocks.Shared` or type `Secrets` not found).

- [ ] **Step 3: Write the implementation**

`src/VoiceReset.Mocks/Shared/Secrets.cs`:

```csharp
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VoiceReset.Mocks.Shared;

/// <summary>
/// Random values and hashing. Everything secret is created with a
/// cryptographic random generator and compared in constant time.
/// </summary>
public static class Secrets
{
    private const int PasswordIterations = 100_000;

    /// <summary>An opaque ID such as <c>rec_1f0c...</c> (96 random bits).</summary>
    public static string NewId(string prefix) =>
        $"{prefix}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";

    /// <summary>A 6-digit verification code chosen by the issuer.</summary>
    public static string NewCode() => FormatCode(RandomNumberGenerator.GetInt32(0, 1_000_000));

    /// <summary>Codes are strings: 47192 becomes "047192".</summary>
    public static string FormatCode(int number) => number.ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>A reset token: 32 random bytes (256 bits), base64url without padding.</summary>
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Constant-time comparison. Use it on hashes, so both sides have the same length.</summary>
    public static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    /// <summary>Salted PBKDF2 hash, stored as "salt.hash" (both base64).</summary>
    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        string[] parts = stored.Split('.');
        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expected = Convert.FromBase64String(parts[1]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Shared.SecretsTests"`
Expected: `Passed!`, `succeeded: 11`, `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VoiceReset.Mocks/Shared/Secrets.cs tests/VoiceReset.Mocks.Tests/Shared/SecretsTests.cs
git commit -m "feat(mocks): add secret and id helpers"
```

---

### Task 2: Strict JSON reader and `ApiResult`

**Files:**
- Create: `src/VoiceReset.Mocks/Shared/MockJson.cs`
- Create: `src/VoiceReset.Mocks/Shared/ApiResult.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Shared/MockJsonTests.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Shared/ApiResultTests.cs`

Why `ApiResult` instead of `TypedResults`: idempotent routes must return the **recorded** response again, byte for byte. A result that is just "status code + JSON text" can be stored and replayed without any mapping.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Shared/MockJsonTests.cs`:

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Tests.Shared;

public sealed class MockJsonTests
{
    public sealed record Sample(string RequestId, string? Note);

    [Fact]
    public async Task ReadAsync_ValidSnakeCaseBody_ReturnsRecord()
    {
        HttpRequest request = JsonRequest("""{"request_id":"r1","note":null}""");

        Sample? sample = await MockJson.ReadAsync<Sample>(request);

        Assert.Equal(new Sample("r1", null), sample);
    }

    [Theory]
    [InlineData("""{"request_id":"r1","note":null,"extra":1}""")]       // unknown field
    [InlineData("""{"request_id":1,"note":null}""")]                    // wrong type
    [InlineData("""{"request_id":true,"note":null}""")]                 // wrong type
    [InlineData("""{"note":null}""")]                                   // missing field
    [InlineData("""{"request_id":"r1"}""")]                             // nullable field is still required
    [InlineData("""{"request_id":null,"note":null}""")]                 // null for a non-null field
    [InlineData("""{"request_id":"a","request_id":"b","note":null}""")] // duplicate field
    [InlineData("""{"requestId":"r1","note":null}""")]                  // wrong naming
    [InlineData("""{not json""")]                                       // malformed
    [InlineData("""null""")]
    [InlineData("""[]""")]
    [InlineData("")]
    public async Task ReadAsync_InvalidBody_ReturnsNull(string body)
    {
        HttpRequest request = JsonRequest(body);

        Sample? sample = await MockJson.ReadAsync<Sample>(request);

        Assert.Null(sample);
    }

    [Fact]
    public async Task ReadAsync_NotJsonContentType_ReturnsNull()
    {
        HttpRequest request = JsonRequest("""{"request_id":"r1","note":null}""", "text/plain");

        Sample? sample = await MockJson.ReadAsync<Sample>(request);

        Assert.Null(sample);
    }

    private static HttpRequest JsonRequest(string body, string contentType = "application/json")
    {
        DefaultHttpContext context = new();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }
}
```

`tests/VoiceReset.Mocks.Tests/Shared/ApiResultTests.cs`:

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Tests.Shared;

public sealed class ApiResultTests
{
    [Fact]
    public async Task ExecuteAsync_Throttled_WritesEnvelopeStatusAndRetryAfter()
    {
        DefaultHttpContext context = new();
        MemoryStream body = new();
        context.Response.Body = body;

        await ApiResult.Throttled(42).ExecuteAsync(context);

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("42", context.Response.Headers.RetryAfter.ToString());
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Equal(
            """{"error":{"code":"throttled","message":"Too many requests. Try again later."}}""",
            Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public void Ok_AnonymousBody_UsesSnakeCaseAndKeepsNulls()
    {
        ApiResult result = ApiResult.Ok(new { RecoveryId = "rec_1", LinkExpiresAt = (DateTimeOffset?)null });

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("""{"recovery_id":"rec_1","link_expires_at":null}""", result.Json);
    }

    [Fact]
    public void VerificationError_Called_AddsAttemptsAndStatusAtTopLevel()
    {
        ApiResult result = ApiResult.VerificationError(422, "verification_failed", "m", 1, "awaiting_verification");

        Assert.Equal(
            """{"error":{"code":"verification_failed","message":"m"},"attempts_remaining":1,"status":"awaiting_verification"}""",
            result.Json);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Shared"`
Expected: build error `CS0103: The name 'MockJson' does not exist in the current context` (and the same for `ApiResult`).

- [ ] **Step 3: Write `MockJson`**

`src/VoiceReset.Mocks/Shared/MockJson.cs`:

```csharp
using System.Text.Json;

namespace VoiceReset.Mocks.Shared;

/// <summary>JSON settings for the contract API and for stored records.</summary>
public static class MockJson
{
    /// <summary>
    /// Requests: the .NET 10 strict preset (unknown and duplicate members rejected,
    /// case-sensitive, nullable annotations and required constructor parameters
    /// respected) plus snake_case names, because the contract uses snake_case.
    /// </summary>
    public static readonly JsonSerializerOptions Requests = new(JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Responses: snake_case names; nulls are written (the contract uses explicit nulls).</summary>
    public static readonly JsonSerializerOptions Responses = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Records saved by the stores. Not part of the contract.</summary>
    public static readonly JsonSerializerOptions Storage = new();

    /// <summary>
    /// Reads a request body strictly. Returns null when the body is not JSON or does
    /// not match the record exactly; the endpoint then answers 400 invalid_request.
    /// </summary>
    public static async Task<T?> ReadAsync<T>(HttpRequest request) where T : class
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, Requests, request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Write `ApiResult`**

`src/VoiceReset.Mocks/Shared/ApiResult.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace VoiceReset.Mocks.Shared;

/// <summary>
/// One HTTP response: status code, JSON body and an optional Retry-After.
/// Plain data, so idempotent results can be stored and replayed exactly.
/// Error messages are fixed texts; they never echo the request.
/// </summary>
public sealed record ApiResult(int StatusCode, string Json, int? RetryAfterSeconds = null) : IResult
{
    public static ApiResult Of(int statusCode, object body) =>
        new(statusCode, JsonSerializer.Serialize(body, MockJson.Responses));

    public static ApiResult Ok(object body) => Of(StatusCodes.Status200OK, body);

    public static object ErrorBody(string code, string message) => new { Code = code, Message = message };

    public static ApiResult Error(int statusCode, string code, string message) =>
        Of(statusCode, new { Error = ErrorBody(code, message) });

    public static ApiResult VerificationError(int statusCode, string code, string message, int attemptsRemaining, string status) =>
        Of(statusCode, new { Error = ErrorBody(code, message), AttemptsRemaining = attemptsRemaining, Status = status });

    public static ApiResult InvalidRequest() =>
        Error(StatusCodes.Status400BadRequest, "invalid_request", "The request is not valid.");

    public static ApiResult Unauthenticated() =>
        Error(StatusCodes.Status401Unauthorized, "unauthenticated", "Authentication is required.");

    public static ApiResult InvalidToken() =>
        Error(StatusCodes.Status401Unauthorized, "invalid_token", "The reset link is not valid.");

    public static ApiResult NotFound() =>
        Error(StatusCodes.Status404NotFound, "not_found", "The resource was not found.");

    public static ApiResult InvalidState(string message) =>
        Error(StatusCodes.Status409Conflict, "invalid_state", message);

    public static ApiResult IdempotencyConflict() =>
        Error(StatusCodes.Status409Conflict, "idempotency_conflict", "This key was already used with a different request.");

    public static ApiResult Throttled(int retryAfterSeconds) =>
        Error(StatusCodes.Status429TooManyRequests, "throttled", "Too many requests. Try again later.")
            with { RetryAfterSeconds = retryAfterSeconds };

    public static ApiResult DependencyUnavailable() =>
        Error(StatusCodes.Status503ServiceUnavailable, "dependency_unavailable", "A dependency is unavailable. The result of the request is unknown.");

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        HttpResponse response = httpContext.Response;
        response.StatusCode = StatusCode;
        response.ContentType = "application/json; charset=utf-8";
        if (RetryAfterSeconds is int seconds)
        {
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        await response.WriteAsync(Json, httpContext.RequestAborted);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Shared"`
Expected: `Passed!`, `failed: 0` (Secrets, MockJson and ApiResult tests).

- [ ] **Step 6: Commit**

```powershell
git add src/VoiceReset.Mocks/Shared tests/VoiceReset.Mocks.Tests/Shared
git commit -m "feat(mocks): add strict json reader and api result"
```

---

### Task 3: Stored records and the in-memory store

**Files:**
- Create: `src/VoiceReset.Mocks/Issuer/RecoveryStatus.cs`
- Create: `src/VoiceReset.Mocks/Tickets/TicketOutcomes.cs`
- Create: `src/VoiceReset.Mocks/Storage/StoredRecord.cs`
- Create: `src/VoiceReset.Mocks/Storage/AccountRecord.cs`
- Create: `src/VoiceReset.Mocks/Storage/RecoveryRecord.cs`
- Create: `src/VoiceReset.Mocks/Storage/IdempotencyRecord.cs`
- Create: `src/VoiceReset.Mocks/Storage/TokenRecord.cs`
- Create: `src/VoiceReset.Mocks/Storage/TicketRecord.cs`
- Create: `src/VoiceReset.Mocks/Storage/InboxMessage.cs`
- Create: `src/VoiceReset.Mocks/Storage/IMockStore.cs`
- Create: `src/VoiceReset.Mocks/Storage/StoreTables.cs`
- Create: `src/VoiceReset.Mocks/Storage/InMemoryMockStore.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Storage/MockStoreContract.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Storage/InMemoryMockStoreTests.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Storage/RecoveryRecordTests.cs`

**Design in one paragraph.** Every saved thing is a small class derived from `StoredRecord`. The store has only four methods: get by key, "try save", add an inbox message, list a user's inbox. "Try save" is the only write: when the record has no `ETag` it inserts (and fails if the key exists); when it has an `ETag` it replaces only if nobody changed the row since it was read. Services read, decide, try to save, and on `false` read again and decide again (at most `StoreRetry.MaxAttempts` times). This one rule gives us atomic attempt counters, single-use tokens and "one ticket per recovery". Status values are plain strings (no enums), so nothing is silently dropped when a stored value is unknown.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Storage/MockStoreContract.cs` (shared by both store implementations; Task 6 adds the Table Storage run):

```csharp
using VoiceReset.Mocks.Issuer;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Storage;

/// <summary>The behaviour every IMockStore must have. Derived classes supply the store.</summary>
public abstract class MockStoreContract
{
    protected abstract Task<IMockStore> CreateStoreAsync();

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        IMockStore store = await CreateStoreAsync();

        RecoveryRecord? loaded = await store.GetAsync<RecoveryRecord>(UniqueKey());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task TrySaveAsync_NewRecord_InsertsAndRoundTripsAllData()
    {
        IMockStore store = await CreateStoreAsync();
        RecoveryRecord record = NewRecovery();
        record.VerifyResults["key-1"] = new StoredResponse("fp", 422, "{}");

        bool saved = await store.TrySaveAsync(record);
        RecoveryRecord? loaded = await store.GetAsync<RecoveryRecord>(record.RecoveryId);

        Assert.True(saved);
        Assert.NotNull(record.ETag);
        Assert.NotNull(loaded);
        Assert.Equal(record.ETag, loaded.ETag);
        Assert.Equal("alice", loaded.Username);
        Assert.Equal(record.VerificationExpiresAt, loaded.VerificationExpiresAt);
        Assert.Equal(RecoveryStatus.AwaitingVerification, loaded.Status);
        Assert.Equal(422, loaded.VerifyResults["key-1"].StatusCode);
    }

    [Fact]
    public async Task TrySaveAsync_InsertExistingKey_ReturnsFalse()
    {
        IMockStore store = await CreateStoreAsync();
        RecoveryRecord first = NewRecovery();
        RecoveryRecord duplicate = NewRecovery(first.RecoveryId);
        await store.TrySaveAsync(first);

        bool saved = await store.TrySaveAsync(duplicate);

        Assert.False(saved);
    }

    [Fact]
    public async Task TrySaveAsync_StaleETag_ReturnsFalseAndKeepsFirstWrite()
    {
        IMockStore store = await CreateStoreAsync();
        RecoveryRecord record = NewRecovery();
        await store.TrySaveAsync(record);
        RecoveryRecord readerA = (await store.GetAsync<RecoveryRecord>(record.RecoveryId))!;
        RecoveryRecord readerB = (await store.GetAsync<RecoveryRecord>(record.RecoveryId))!;
        readerA.Status = RecoveryStatus.Verified;
        readerB.Status = RecoveryStatus.Exhausted;

        bool firstSaved = await store.TrySaveAsync(readerA);
        bool secondSaved = await store.TrySaveAsync(readerB);
        RecoveryRecord? reloaded = await store.GetAsync<RecoveryRecord>(record.RecoveryId);

        Assert.True(firstSaved);
        Assert.False(secondSaved);
        Assert.Equal(RecoveryStatus.Verified, reloaded?.Status);
    }

    [Fact]
    public async Task TrySaveAsync_SameKeyInDifferentTables_DoesNotClash()
    {
        IMockStore store = await CreateStoreAsync();
        string key = UniqueKey();

        bool recoverySaved = await store.TrySaveAsync(NewRecovery(key));
        bool accountSaved = await store.TrySaveAsync(new AccountRecord { Username = key });

        Assert.True(recoverySaved);
        Assert.True(accountSaved);
    }

    [Fact]
    public async Task TrySaveAsync_KeyWithCharactersTablesForbid_RoundTrips()
    {
        IMockStore store = await CreateStoreAsync();
        string key = $"a/b#c?d\\e {UniqueKey()}";

        await store.TrySaveAsync(new IdempotencyRecord { Action = "start", IdempotencyKey = key, Fingerprint = "fp" });
        IdempotencyRecord? loaded = await store.GetAsync<IdempotencyRecord>(IdempotencyRecord.KeyFor("start", key));

        Assert.Equal(key, loaded?.IdempotencyKey);
    }

    [Fact]
    public async Task GetInboxAsync_TwoUsers_ReturnsOnlyThatUsersMessagesNewestFirst()
    {
        IMockStore store = await CreateStoreAsync();
        string alice = UniqueKey();
        string bob = UniqueKey();
        DateTimeOffset start = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        await store.AddInboxMessageAsync(Message(alice, "older", start));
        await store.AddInboxMessageAsync(Message(bob, "other-user", start.AddSeconds(5)));
        await store.AddInboxMessageAsync(Message(alice, "newer", start.AddSeconds(10)));

        IReadOnlyList<InboxMessage> inbox = await store.GetInboxAsync(alice, 50);

        Assert.Equal(new[] { "newer", "older" }, inbox.Select(m => m.Code).ToArray());
    }

    protected static string UniqueKey() => $"test_{Guid.NewGuid():N}";

    private static RecoveryRecord NewRecovery(string? id = null) => new()
    {
        RecoveryId = id ?? UniqueKey(),
        Username = "alice",
        IsDecoy = false,
        VerificationExpiresAt = new DateTimeOffset(2026, 10, 3, 9, 2, 0, TimeSpan.Zero),
        AttemptsRemaining = 2,
    };

    private static InboxMessage Message(string username, string code, DateTimeOffset at) =>
        new(username, $"msg_{Guid.NewGuid():N}", at, InboxMessageKinds.VerificationCode, code, null, at.AddMinutes(2));
}
```

`tests/VoiceReset.Mocks.Tests/Storage/InMemoryMockStoreTests.cs`:

```csharp
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Storage;

public sealed class InMemoryMockStoreTests : MockStoreContract
{
    protected override Task<IMockStore> CreateStoreAsync() => Task.FromResult<IMockStore>(new InMemoryMockStore());
}
```

`tests/VoiceReset.Mocks.Tests/Storage/RecoveryRecordTests.cs`:

```csharp
using VoiceReset.Mocks.Issuer;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Storage;

public sealed class RecoveryRecordTests
{
    private static readonly DateTimeOffset Deadline = new(2026, 10, 3, 9, 2, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RecoveryStatus.AwaitingVerification, -1, RecoveryStatus.AwaitingVerification)]
    [InlineData(RecoveryStatus.AwaitingVerification, 0, RecoveryStatus.Expired)]
    [InlineData(RecoveryStatus.Verified, 0, RecoveryStatus.Expired)]
    [InlineData(RecoveryStatus.Exhausted, 600, RecoveryStatus.Exhausted)]
    [InlineData(RecoveryStatus.Completed, 600, RecoveryStatus.Completed)]
    public void StatusAt_SecondsAfterDeadline_ReturnsExpectedStatus(string stored, int secondsAfterDeadline, string expected)
    {
        RecoveryRecord record = Recovery(stored);

        string status = record.StatusAt(Deadline.AddSeconds(secondsAfterDeadline));

        Assert.Equal(expected, status);
    }

    [Fact]
    public void StatusAt_LinkIssuedAfterLinkExpiry_ReturnsExpired()
    {
        RecoveryRecord record = Recovery(RecoveryStatus.LinkIssued);
        record.LinkExpiresAt = Deadline.AddMinutes(10);

        string status = record.StatusAt(Deadline.AddMinutes(10));

        Assert.Equal(RecoveryStatus.Expired, status);
    }

    [Theory]
    [InlineData(RecoveryStatus.Completed)]
    [InlineData(RecoveryStatus.ResetFailed)]
    public void ActiveUntil_TerminalStatus_ReturnsNull(string stored)
    {
        RecoveryRecord record = Recovery(stored);

        DateTimeOffset? activeUntil = record.ActiveUntil();

        Assert.Null(activeUntil);
    }

    [Fact]
    public void ActiveUntil_Exhausted_ReturnsVerificationDeadline()
    {
        RecoveryRecord record = Recovery(RecoveryStatus.Exhausted);

        DateTimeOffset? activeUntil = record.ActiveUntil();

        Assert.Equal(Deadline, activeUntil);
    }

    private static RecoveryRecord Recovery(string status) => new()
    {
        RecoveryId = "rec_1",
        Username = "alice",
        IsDecoy = false,
        VerificationExpiresAt = Deadline,
        Status = status,
    };
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Storage"`
Expected: build error `CS0234: The type or namespace name 'Storage' does not exist in the namespace 'VoiceReset.Mocks'`.

- [ ] **Step 3: Write the status constants**

`src/VoiceReset.Mocks/Issuer/RecoveryStatus.cs`:

```csharp
namespace VoiceReset.Mocks.Issuer;

/// <summary>Recovery status values, exactly as the mock contract names them.</summary>
public static class RecoveryStatus
{
    public const string AwaitingVerification = "awaiting_verification";
    public const string Verified = "verified";
    public const string LinkIssued = "link_issued";
    public const string ResetPending = "reset_pending";
    public const string Completed = "completed";

    /// <summary>Part of the contract. This mock has no failure fault yet, so nothing sets it (see the docs).</summary>
    public const string ResetFailed = "reset_failed";

    public const string Exhausted = "exhausted";

    /// <summary>Never stored: computed from the deadlines by <c>RecoveryRecord.StatusAt</c>.</summary>
    public const string Expired = "expired";
}

/// <summary>Values of <c>unlock_status</c> on a successful reset.</summary>
public static class UnlockStatuses
{
    public const string Unlocked = "unlocked";
    public const string NotRequired = "not_required";
}
```

`src/VoiceReset.Mocks/Tickets/TicketOutcomes.cs`:

```csharp
namespace VoiceReset.Mocks.Tickets;

/// <summary>Ticket outcomes and reason codes from the mock contract.</summary>
public static class TicketOutcomes
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Escalated = "escalated";
    public const string Cancelled = "cancelled";
    public const string Pending = "pending";

    public const string ResetCompleted = "reset_completed";
    public const string HumanRequested = "human_requested";

    /// <summary>Reasons allowed for every outcome except "resolved".</summary>
    public static readonly IReadOnlySet<string> SafeReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "browser_unavailable",
        "verification_exhausted",
        "verification_expired",
        HumanRequested,
        "caller_cancelled",
        "call_dropped",
        "dependency_unavailable",
        "completion_unknown",
    };
}
```

- [ ] **Step 4: Write the records**

`src/VoiceReset.Mocks/Storage/StoredRecord.cs`:

```csharp
using System.Text.Json.Serialization;

namespace VoiceReset.Mocks.Storage;

/// <summary>
/// Base class for everything the stores save. The store fills <see cref="ETag"/>
/// when it reads a record and after a successful save; <c>TrySaveAsync</c> uses it
/// for optimistic concurrency.
/// </summary>
public abstract class StoredRecord
{
    [JsonIgnore]
    public string? ETag { get; set; }

    /// <summary>The unique key of the record inside its table.</summary>
    public abstract string GetKey();
}
```

`src/VoiceReset.Mocks/Storage/AccountRecord.cs`:

```csharp
namespace VoiceReset.Mocks.Storage;

/// <summary>
/// One row per normalised username, for known AND unknown usernames, so
/// throttling behaves the same for decoys. Restarts cannot reset it.
/// </summary>
public sealed class AccountRecord : StoredRecord
{
    public required string Username { get; init; }

    public string? ActiveRecoveryId { get; set; }

    public DateTimeOffset? LastCodeIssuedAt { get; set; }

    /// <summary>Null until the first reset; until then the configured initial password is current.</summary>
    public string? PasswordHash { get; set; }

    public override string GetKey() => Username;
}
```

`src/VoiceReset.Mocks/Storage/RecoveryRecord.cs`:

```csharp
using VoiceReset.Mocks.Issuer;

namespace VoiceReset.Mocks.Storage;

/// <summary>
/// One recovery attempt, from the start request to the reset receipt.
/// It holds hashes only: never a code, token or password in plain text.
/// Terminal records are kept for reconciliation.
/// </summary>
public sealed class RecoveryRecord : StoredRecord
{
    public required string RecoveryId { get; init; }

    public required string Username { get; init; }

    /// <summary>True for an unknown username: same envelope, no delivery, verification always fails.</summary>
    public required bool IsDecoy { get; init; }

    public required DateTimeOffset VerificationExpiresAt { get; init; }

    public string Status { get; set; } = RecoveryStatus.AwaitingVerification;

    public int AttemptsRemaining { get; set; }

    /// <summary>SHA-256 of "recoveryId\ncode"; null for decoys and after verification or exhaustion.</summary>
    public string? CodeHash { get; set; }

    /// <summary>Recorded verification results by Idempotency-Key (saved atomically with the counter).</summary>
    public Dictionary<string, StoredResponse> VerifyResults { get; set; } = [];

    public string? LinkOperationId { get; set; }

    public string? TokenHash { get; set; }

    public DateTimeOffset? LinkExpiresAt { get; set; }

    public string? ResetOperationId { get; set; }

    /// <summary>SHA-256 of "token\nnew password": recognises an identical retry without keeping the password.</summary>
    public string? ResetFingerprint { get; set; }

    public string? NewPasswordHash { get; set; }

    public DateTimeOffset? ResetCompletesAt { get; set; }

    public string? ResetReceipt { get; set; }

    public string? UnlockStatus { get; set; }

    public override string GetKey() => RecoveryId;

    /// <summary>The status as the contract reports it at <paramref name="now"/>: deadlines turn into "expired".</summary>
    public string StatusAt(DateTimeOffset now) => Status switch
    {
        RecoveryStatus.AwaitingVerification or RecoveryStatus.Verified when now >= VerificationExpiresAt => RecoveryStatus.Expired,
        RecoveryStatus.LinkIssued when now >= LinkExpiresAt => RecoveryStatus.Expired,
        _ => Status,
    };

    /// <summary>
    /// Until when this recovery blocks a new start for the same account
    /// (null = not active). Exhausted recoveries block until the verification deadline.
    /// </summary>
    public DateTimeOffset? ActiveUntil() => Status switch
    {
        RecoveryStatus.AwaitingVerification or RecoveryStatus.Verified or RecoveryStatus.Exhausted => VerificationExpiresAt,
        RecoveryStatus.LinkIssued => LinkExpiresAt,
        RecoveryStatus.ResetPending => ResetCompletesAt,
        _ => null,
    };
}

/// <summary>A recorded response for an idempotency key, plus the fingerprint of the request that produced it.</summary>
public sealed record StoredResponse(string Fingerprint, int StatusCode, string Json);
```

`src/VoiceReset.Mocks/Storage/IdempotencyRecord.cs`:

```csharp
namespace VoiceReset.Mocks.Storage;

/// <summary>
/// The recorded result of one logical request, by action and key:
/// "start" + request_id, "ticket-create" + operation_id, and "reset" + operation_id
/// (for "reset" it is the index from operation ID to recovery; the result itself is
/// rebuilt from the recovery because a pending reset can complete later).
/// </summary>
public sealed class IdempotencyRecord : StoredRecord
{
    public required string Action { get; init; }

    public required string IdempotencyKey { get; init; }

    /// <summary>SHA-256 of the request content, to detect the same key with a different request.</summary>
    public required string Fingerprint { get; init; }

    public string? RecoveryId { get; init; }

    public int StatusCode { get; init; }

    public string Json { get; init; } = "";

    public override string GetKey() => KeyFor(Action, IdempotencyKey);

    public static string KeyFor(string action, string idempotencyKey) => $"{action}:{idempotencyKey}";
}
```

`src/VoiceReset.Mocks/Storage/TokenRecord.cs`:

```csharp
namespace VoiceReset.Mocks.Storage;

/// <summary>Finds the recovery for a reset token. Keyed by the token's SHA-256; the token itself is never stored.</summary>
public sealed class TokenRecord : StoredRecord
{
    public required string TokenHash { get; init; }

    public required string RecoveryId { get; init; }

    public override string GetKey() => TokenHash;
}
```

`src/VoiceReset.Mocks/Storage/TicketRecord.cs`:

```csharp
using VoiceReset.Mocks.Tickets;

namespace VoiceReset.Mocks.Storage;

/// <summary>The one ticket of a recovery, with its full outcome history.</summary>
public sealed class TicketRecord : StoredRecord
{
    public required string TicketId { get; init; }

    public required string RecoveryId { get; init; }

    public string Outcome { get; set; } = TicketOutcomes.Open;

    public string? ReasonCode { get; set; }

    public string? ResetReceipt { get; set; }

    /// <summary>Every accepted update, in order. Also the idempotency record for outcome updates.</summary>
    public List<TicketHistoryEntry> History { get; set; } = [];

    public override string GetKey() => TicketId;
}

/// <summary>One outcome update. <c>ChangedOutcome</c> is false when the rules kept the earlier outcome.</summary>
public sealed record TicketHistoryEntry(
    string OperationId,
    string Fingerprint,
    string Outcome,
    string ReasonCode,
    bool ChangedOutcome,
    DateTimeOffset At,
    int StatusCode,
    string Json);
```

`src/VoiceReset.Mocks/Storage/InboxMessage.cs`:

```csharp
namespace VoiceReset.Mocks.Storage;

/// <summary>A message in a synthetic user's mock inbox: a verification code or a reset link.</summary>
public sealed record InboxMessage(
    string Username,
    string MessageId,
    DateTimeOffset CreatedAt,
    string Kind,
    string? Code,
    string? Link,
    DateTimeOffset ExpiresAt);

public static class InboxMessageKinds
{
    public const string VerificationCode = "verification_code";
    public const string ResetLink = "reset_link";
}
```

- [ ] **Step 5: Write the store interface and table names**

`src/VoiceReset.Mocks/Storage/IMockStore.cs`:

```csharp
namespace VoiceReset.Mocks.Storage;

/// <summary>
/// The mocks' only I/O boundary. Two implementations: Table Storage (Azure) and
/// in-memory (tests, or when no table endpoint is configured).
/// </summary>
public interface IMockStore
{
    Task<T?> GetAsync<T>(string key) where T : StoredRecord;

    /// <summary>
    /// Insert when <c>record.ETag</c> is null (false if the key already exists).
    /// Otherwise replace, but only if the stored ETag still matches (false if
    /// someone else wrote first). On success <c>record.ETag</c> is updated.
    /// </summary>
    Task<bool> TrySaveAsync<T>(T record) where T : StoredRecord;

    Task AddInboxMessageAsync(InboxMessage message);

    /// <summary>The user's messages, newest first.</summary>
    Task<IReadOnlyList<InboxMessage>> GetInboxAsync(string username, int maxMessages);
}

/// <summary>How often a service re-reads and retries after losing an ETag race.</summary>
public static class StoreRetry
{
    public const int MaxAttempts = 5;
}
```

`src/VoiceReset.Mocks/Storage/StoreTables.cs`:

```csharp
namespace VoiceReset.Mocks.Storage;

/// <summary>Table names (also used as prefixes by the in-memory store).</summary>
public static class StoreTables
{
    public const string Inbox = "inbox";

    public static readonly IReadOnlyList<string> All =
        ["accounts", "recoveries", "idempotency", "tokens", "tickets", Inbox];

    public static string NameFor<T>() where T : StoredRecord => typeof(T).Name switch
    {
        nameof(AccountRecord) => "accounts",
        nameof(RecoveryRecord) => "recoveries",
        nameof(IdempotencyRecord) => "idempotency",
        nameof(TokenRecord) => "tokens",
        nameof(TicketRecord) => "tickets",
        _ => throw new InvalidOperationException($"No table is defined for {typeof(T).Name}."),
    };
}
```

- [ ] **Step 6: Write the in-memory store**

`src/VoiceReset.Mocks/Storage/InMemoryMockStore.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Storage;

/// <summary>
/// Keeps records as JSON text with a version number, so it behaves like Table
/// Storage: every read returns a fresh copy, and the version acts as the ETag.
/// Data is lost when the process stops.
/// </summary>
public sealed class InMemoryMockStore : IMockStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Json, long Version)> _rows = [];
    private readonly List<InboxMessage> _inbox = [];

    public Task<T?> GetAsync<T>(string key) where T : StoredRecord
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(RowKey<T>(key), out (string Json, long Version) row))
            {
                return Task.FromResult<T?>(null);
            }

            T? record = JsonSerializer.Deserialize<T>(row.Json, MockJson.Storage);
            if (record is not null)
            {
                record.ETag = row.Version.ToString(CultureInfo.InvariantCulture);
            }

            return Task.FromResult(record);
        }
    }

    public Task<bool> TrySaveAsync<T>(T record) where T : StoredRecord
    {
        string rowKey = RowKey<T>(record.GetKey());
        lock (_gate)
        {
            bool exists = _rows.TryGetValue(rowKey, out (string Json, long Version) current);
            bool allowed = record.ETag is null
                ? !exists
                : exists && current.Version.ToString(CultureInfo.InvariantCulture) == record.ETag;
            if (!allowed)
            {
                return Task.FromResult(false);
            }

            long version = exists ? current.Version + 1 : 1;
            _rows[rowKey] = (JsonSerializer.Serialize(record, MockJson.Storage), version);
            record.ETag = version.ToString(CultureInfo.InvariantCulture);
            return Task.FromResult(true);
        }
    }

    public Task AddInboxMessageAsync(InboxMessage message)
    {
        lock (_gate)
        {
            _inbox.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InboxMessage>> GetInboxAsync(string username, int maxMessages)
    {
        lock (_gate)
        {
            // Reverse first so that, for equal times, the message added last comes first.
            IReadOnlyList<InboxMessage> messages = Enumerable.Reverse(_inbox)
                .Where(message => message.Username == username)
                .OrderByDescending(message => message.CreatedAt)
                .Take(maxMessages)
                .ToList();
            return Task.FromResult(messages);
        }
    }

    private static string RowKey<T>(string key) where T : StoredRecord => $"{StoreTables.NameFor<T>()}|{key}";
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Storage"`
Expected: `Passed!`, `succeeded: 16`, `failed: 0`.

- [ ] **Step 8: Commit**

```powershell
git add src/VoiceReset.Mocks/Issuer src/VoiceReset.Mocks/Tickets src/VoiceReset.Mocks/Storage tests/VoiceReset.Mocks.Tests/Storage
git commit -m "feat(mocks): add stored records and in-memory store"
```

---

### Task 4: Configuration, start-up validation, health endpoint and the test factory

**Files:**
- Create: `src/VoiceReset.Mocks/Configuration/MocksOptions.cs`
- Create: `src/VoiceReset.Mocks/Configuration/FaultsOptions.cs`
- Create: `src/VoiceReset.Mocks/Configuration/MocksOptionsValidator.cs`
- Create: `src/VoiceReset.Mocks/Configuration/SyntheticUsers.cs`
- Create: `src/VoiceReset.Mocks/Shared/HealthEndpoints.cs`
- Create: `src/VoiceReset.Mocks/appsettings.json` (replace the template file if it exists)
- Modify: `src/VoiceReset.Mocks/Program.cs` (full replacement)
- Create: `tests/VoiceReset.Mocks.Tests/MocksFactory.cs`
- Create: `tests/VoiceReset.Mocks.Tests/CapturingLoggerProvider.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Configuration/OptionsValidationTests.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Shared/HealthTests.cs`

- [ ] **Step 1: Write the test helpers**

`tests/VoiceReset.Mocks.Tests/CapturingLoggerProvider.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VoiceReset.Mocks.Tests;

/// <summary>Hand-written fake: keeps every log line (level, category, event id, message, exception).</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category} [{eventId.Id}] {formatter(state, exception)} {exception}");
    }
}
```

`tests/VoiceReset.Mocks.Tests/MocksFactory.cs`:

```csharp
using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests;

/// <summary>
/// Runs the real mocks app in memory with test configuration, a fake clock,
/// a store the test can share (to simulate a restart) and captured logs.
/// alice requires an unlock; bob does not.
/// </summary>
public sealed class MocksFactory : WebApplicationFactory<Program>
{
    public const string ServiceCredential = "test-service-credential-0123456789abcdef";
    public const string AllowedOrigin = "https://agent.test";
    public const string ResetBaseUrl = "https://agent.test/reset/";
    public const string AliceInboxPassword = "alice-inbox-password";
    public const string AliceInitialPassword = "First-Synthetic-Pass-11";
    public const string BobInboxPassword = "bob-inbox-password-x";
    public const string BobInitialPassword = "Other-Synthetic-Pass-22";

    public static readonly DateTimeOffset StartTime = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string?> _settings;

    public MocksFactory(IMockStore? store = null, int resetDelaySeconds = 0, Dictionary<string, string?>? overrides = null)
    {
        Store = store ?? new InMemoryMockStore();
        _settings = new Dictionary<string, string?>
        {
            ["Mocks:ServiceCredential"] = ServiceCredential,
            ["Mocks:ResetBaseUrl"] = ResetBaseUrl,
            ["Mocks:AllowedCorsOrigin"] = AllowedOrigin,
            ["Mocks:Users:0:Username"] = "alice",
            ["Mocks:Users:0:DisplayName"] = "Alice Example",
            ["Mocks:Users:0:RequiresUnlock"] = "true",
            ["Mocks:Users:0:InboxPassword"] = AliceInboxPassword,
            ["Mocks:Users:0:InitialPassword"] = AliceInitialPassword,
            ["Mocks:Users:1:Username"] = "bob",
            ["Mocks:Users:1:DisplayName"] = "Bob Example",
            ["Mocks:Users:1:RequiresUnlock"] = "false",
            ["Mocks:Users:1:InboxPassword"] = BobInboxPassword,
            ["Mocks:Users:1:InitialPassword"] = BobInitialPassword,
            ["Faults:ResetCompletionDelaySeconds"] = resetDelaySeconds.ToString(CultureInfo.InvariantCulture),
            ["Storage:TableEndpoint"] = "",
        };
        if (overrides is not null)
        {
            foreach (KeyValuePair<string, string?> setting in overrides)
            {
                _settings[setting.Key] = setting.Value;
            }
        }

        // Cookies are Secure, so the in-memory client must use https.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    public IMockStore Store { get; }

    public FakeTimeProvider Time { get; } = new(StartTime);

    public CapturingLoggerProvider Logs { get; } = new();

    public HttpClient CreateServiceClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceCredential);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton(Store);
        });
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Configuration/OptionsValidationTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Configuration;

namespace VoiceReset.Mocks.Tests.Configuration;

public sealed class OptionsValidationTests
{
    private const string ValidCredential = "service-credential-0123456789abcdef";
    private const string KeyVaultReference = "@Microsoft.KeyVault(VaultName=kv;SecretName=x)";

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        ValidateOptionsResult result = Validate(Options());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ShortServiceCredential_ReportsKeyWithoutValue()
    {
        ValidateOptionsResult result = Validate(Options(serviceCredential: "short-secret"));

        Assert.True(result.Failed);
        Assert.Contains("Mocks:ServiceCredential must be at least 32 characters", result.FailureMessage);
        Assert.DoesNotContain("short-secret", result.FailureMessage);
    }

    [Fact]
    public void Validate_KeyVaultReferenceAsServiceCredential_ReportsUnresolvedReference()
    {
        ValidateOptionsResult result = Validate(Options(serviceCredential: KeyVaultReference + "-padding-to-be-long-enough"));

        Assert.Contains("Mocks:ServiceCredential is an unresolved Key Vault reference", result.FailureMessage);
    }

    [Fact]
    public void Validate_KeyVaultReferenceAsInboxPassword_ReportsUnresolvedReference()
    {
        ValidateOptionsResult result = Validate(Options(users: [User(inboxPassword: KeyVaultReference)]));

        Assert.Contains("Mocks:Users:0:InboxPassword is an unresolved Key Vault reference", result.FailureMessage);
    }

    [Fact]
    public void Validate_KeyVaultReferenceAsInitialPassword_ReportsUnresolvedReference()
    {
        ValidateOptionsResult result = Validate(Options(users: [User(initialPassword: KeyVaultReference)]));

        Assert.Contains("Mocks:Users:0:InitialPassword is an unresolved Key Vault reference", result.FailureMessage);
    }

    [Theory]
    [InlineData("http://agent.test/reset/")]
    [InlineData("https://agent.test/reset/#token=x")]
    [InlineData("https://agent.test/reset/?a=1")]
    [InlineData("not a url")]
    public void Validate_BadResetBaseUrl_ReportsResetBaseUrl(string resetBaseUrl)
    {
        ValidateOptionsResult result = Validate(Options(resetBaseUrl: resetBaseUrl));

        Assert.Contains("Mocks:ResetBaseUrl", result.FailureMessage);
    }

    [Theory]
    [InlineData("https://agent.test/")]
    [InlineData("https://agent.test/reset")]
    [InlineData("http://agent.test")]
    public void Validate_BadCorsOrigin_ReportsCorsOrigin(string origin)
    {
        ValidateOptionsResult result = Validate(Options(corsOrigin: origin));

        Assert.Contains("Mocks:AllowedCorsOrigin", result.FailureMessage);
    }

    [Fact]
    public void Validate_SameUsernameInDifferentCase_ReportsDuplicate()
    {
        ValidateOptionsResult result = Validate(Options(users: [User("alice"), User("ALICE")]));

        Assert.Contains("Mocks:Users:1:Username is used twice", result.FailureMessage);
    }

    [Fact]
    public void Validate_NoUsers_ReportsUsers()
    {
        ValidateOptionsResult result = Validate(Options(users: []));

        Assert.Contains("Mocks:Users must contain at least one synthetic user", result.FailureMessage);
    }

    [Fact]
    public void CreateClient_InvalidServiceCredential_AppFailsToStart()
    {
        using MocksFactory factory = new(overrides: new() { ["Mocks:ServiceCredential"] = "too-short" });

        Exception? error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("Mocks:ServiceCredential", error.ToString());
    }

    [Fact]
    public void CreateClient_NegativeResetDelay_AppFailsToStart()
    {
        using MocksFactory factory = new(resetDelaySeconds: -1);

        Exception? error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.Contains("ResetCompletionDelaySeconds", error.ToString());
    }

    private static ValidateOptionsResult Validate(MocksOptions options) => new MocksOptionsValidator().Validate(null, options);

    private static MocksOptions Options(
        string serviceCredential = ValidCredential,
        string resetBaseUrl = "https://agent.test/reset/",
        string corsOrigin = "https://agent.test",
        List<MockUserOptions>? users = null) => new()
    {
        ServiceCredential = serviceCredential,
        ResetBaseUrl = resetBaseUrl,
        AllowedCorsOrigin = corsOrigin,
        Users = users ?? [User()],
    };

    private static MockUserOptions User(
        string username = "alice",
        string inboxPassword = "alice-inbox-password",
        string initialPassword = "Alice-Initial-Password-1") => new()
    {
        Username = username,
        DisplayName = "Alice Example",
        RequiresUnlock = true,
        InboxPassword = inboxPassword,
        InitialPassword = initialPassword,
    };
}
```

`tests/VoiceReset.Mocks.Tests/Shared/HealthTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Tests.Shared;

public sealed class HealthTests
{
    [Fact]
    public async Task GetHealth_AppRunning_ReturnsOkAndCommit()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("commit").GetString()));
    }

    [Theory]
    [InlineData("1.0.0+3f2a9c1d", "3f2a9c1d")]
    [InlineData("1.0.0", "unknown")]
    [InlineData("1.0.0+", "unknown")]
    [InlineData(null, "unknown")]
    public void ParseCommit_InformationalVersion_ReturnsShaAfterPlus(string? informationalVersion, string expected)
    {
        string commit = HealthEndpoints.ParseCommit(informationalVersion);

        Assert.Equal(expected, commit);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Configuration.OptionsValidationTests" --filter-class "VoiceReset.Mocks.Tests.Shared.HealthTests"`
Expected: build error `CS0234: The type or namespace name 'Configuration' does not exist in the namespace 'VoiceReset.Mocks'`.

- [ ] **Step 4: Write the options classes**

`src/VoiceReset.Mocks/Configuration/MocksOptions.cs`:

```csharp
namespace VoiceReset.Mocks.Configuration;

/// <summary>Section "Mocks". Secrets arrive as App Service Key Vault references.</summary>
public sealed class MocksOptions
{
    public const string SectionName = "Mocks";

    /// <summary>The one namespace-scoped Bearer credential (shared with app-agent). Secret.</summary>
    public string ServiceCredential { get; init; } = "";

    /// <summary>The registered reset form URL; links are "{ResetBaseUrl}#token=...".</summary>
    public string ResetBaseUrl { get; init; } = "";

    /// <summary>The only browser origin allowed by CORS (the app-agent origin).</summary>
    public string AllowedCorsOrigin { get; init; } = "";

    public List<MockUserOptions> Users { get; init; } = [];
}

/// <summary>One pre-enrolled synthetic user.</summary>
public sealed class MockUserOptions
{
    public string Username { get; init; } = "";

    public string DisplayName { get; init; } = "";

    /// <summary>When true, a successful reset reports unlock_status "unlocked".</summary>
    public bool RequiresUnlock { get; init; }

    /// <summary>Password for the mock inbox page. Secret.</summary>
    public string InboxPassword { get; init; } = "";

    /// <summary>Current synthetic password until the first reset (for "not_current_password"). Secret.</summary>
    public string InitialPassword { get; init; } = "";
}
```

`src/VoiceReset.Mocks/Configuration/FaultsOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace VoiceReset.Mocks.Configuration;

/// <summary>Section "Faults": switches that make the mocks behave like a slow or failing system.</summary>
public sealed class FaultsOptions
{
    public const string SectionName = "Faults";

    /// <summary>0 = a reset completes at once (200). Above 0 = it answers 202 pending and completes after this many seconds.</summary>
    [Range(0, 3600)]
    public int ResetCompletionDelaySeconds { get; init; }
}
```

`src/VoiceReset.Mocks/Configuration/MocksOptionsValidator.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace VoiceReset.Mocks.Configuration;

/// <summary>
/// Runs at start-up (ValidateOnStart): a wrong setting stops the app with a clear
/// message instead of failing during a call. Messages name the key, never the value.
/// </summary>
public sealed partial class MocksOptionsValidator : IValidateOptions<MocksOptions>
{
    // App Service passes this literal text when it cannot resolve a Key Vault reference.
    private const string UnresolvedKeyVaultReference = "@Microsoft.KeyVault(";

    public ValidateOptionsResult Validate(string? name, MocksOptions options)
    {
        List<string> errors = [];

        CheckSecret(errors, "Mocks:ServiceCredential", options.ServiceCredential, minimumLength: 32);

        if (!IsHttpsUrl(options.ResetBaseUrl, out Uri? resetUrl) || resetUrl.Query.Length > 0 || resetUrl.Fragment.Length > 0)
        {
            errors.Add("Mocks:ResetBaseUrl must be an absolute HTTPS URL without query or fragment.");
        }

        if (!IsHttpsUrl(options.AllowedCorsOrigin, out Uri? origin) || origin.PathAndQuery != "/" || options.AllowedCorsOrigin.EndsWith('/'))
        {
            errors.Add("Mocks:AllowedCorsOrigin must be an HTTPS origin without a path, for example https://app.example.net.");
        }

        if (options.Users.Count == 0)
        {
            errors.Add("Mocks:Users must contain at least one synthetic user.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int index = 0; index < options.Users.Count; index++)
        {
            MockUserOptions user = options.Users[index];
            string prefix = $"Mocks:Users:{index}";
            string username = SyntheticUsers.Normalize(user.Username);

            if (!UsernamePattern().IsMatch(username))
            {
                errors.Add($"{prefix}:Username must match ^[a-z0-9._-]{{3,64}}$.");
            }
            else if (!seen.Add(username))
            {
                errors.Add($"{prefix}:Username is used twice.");
            }

            if (string.IsNullOrWhiteSpace(user.DisplayName))
            {
                errors.Add($"{prefix}:DisplayName is required.");
            }

            CheckSecret(errors, $"{prefix}:InboxPassword", user.InboxPassword, minimumLength: 12);
            CheckSecret(errors, $"{prefix}:InitialPassword", user.InitialPassword, minimumLength: 1);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void CheckSecret(List<string> errors, string key, string value, int minimumLength)
    {
        if (value.StartsWith(UnresolvedKeyVaultReference, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{key} is an unresolved Key Vault reference. Check the managed identity and the secret name.");
        }
        else if (value.Length < minimumLength)
        {
            errors.Add($"{key} must be at least {minimumLength} characters.");
        }
    }

    private static bool IsHttpsUrl(string value, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;

    [GeneratedRegex("^[a-z0-9._-]{3,64}$")]
    private static partial Regex UsernamePattern();
}
```

`src/VoiceReset.Mocks/Configuration/SyntheticUsers.cs`:

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Configuration;

/// <summary>Looks up the pre-enrolled synthetic users from configuration.</summary>
public sealed class SyntheticUsers(IOptions<MocksOptions> options)
{
    // Compared when the username is unknown, so a wrong username takes as long as a wrong password.
    private const string UnknownUserPlaceholder = "unknown-user-placeholder";

    /// <summary>"  Alice " and "alice" are the same account.</summary>
    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public MockUserOptions? Find(string username)
    {
        string normalized = Normalize(username);
        return options.Value.Users.FirstOrDefault(user => Normalize(user.Username) == normalized);
    }

    /// <summary>Returns the user when the inbox password matches (constant-time compare of hashes).</summary>
    public MockUserOptions? CheckInboxLogin(string username, string password)
    {
        MockUserOptions? user = Find(username);
        bool passwordMatches = Secrets.FixedTimeEquals(
            Secrets.Sha256Hex(user?.InboxPassword ?? UnknownUserPlaceholder),
            Secrets.Sha256Hex(password));
        return user is not null && passwordMatches ? user : null;
    }
}
```

- [ ] **Step 5: Write the health endpoint**

`src/VoiceReset.Mocks/Shared/HealthEndpoints.cs`:

```csharp
using System.Reflection;

namespace VoiceReset.Mocks.Shared;

/// <summary>GET /health: lets anyone compare the deployment with the pinned commit.</summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        string commit = ParseCommit(typeof(HealthEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        app.MapGet("/health", () => ApiResult.Ok(new { Status = "ok", Commit = commit }));
    }

    /// <summary>"1.0.0+3f2a..." gives "3f2a...": the build appends SourceRevisionId after the '+'.</summary>
    public static string ParseCommit(string? informationalVersion)
    {
        if (informationalVersion is null)
        {
            return "unknown";
        }

        int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 && plus < informationalVersion.Length - 1 ? informationalVersion[(plus + 1)..] : "unknown";
    }
}
```

- [ ] **Step 6: Write `appsettings.json` and `Program.cs`**

`src/VoiceReset.Mocks/appsettings.json` (shape only; real values are app settings, secrets are Key Vault references):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Mocks": {
    "ServiceCredential": "",
    "ResetBaseUrl": "",
    "AllowedCorsOrigin": "",
    "Users": []
  },
  "Storage": {
    "TableEndpoint": ""
  },
  "Faults": {
    "ResetCompletionDelaySeconds": 0
  }
}
```

`src/VoiceReset.Mocks/Program.cs` (full replacement):

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Shared;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Time and configuration
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<MocksOptions>()
    .Bind(builder.Configuration.GetSection(MocksOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MocksOptions>, MocksOptionsValidator>();
builder.Services.AddOptions<FaultsOptions>()
    .Bind(builder.Configuration.GetSection(FaultsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<SyntheticUsers>();

WebApplication app = builder.Build();

// Any unexpected failure (for example storage down) is reported as the contract's 503.
app.UseExceptionHandler(errorApp => errorApp.Run(context => ApiResult.DependencyUnavailable().ExecuteAsync(context)));

app.MapHealthEndpoints();

await app.RunAsync();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Configuration.OptionsValidationTests" --filter-class "VoiceReset.Mocks.Tests.Shared.HealthTests"`
Expected: `Passed!`, `succeeded: 21`, `failed: 0`.

- [ ] **Step 8: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests
git commit -m "feat(mocks): add configuration validation and health endpoint"
```

---

### Task 5: Table Storage store, credential choice and log messages

**Files:**
- Create: `src/VoiceReset.Mocks/Storage/TableMockStore.cs`
- Create: `src/VoiceReset.Mocks/Shared/MockLog.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Storage/TableMockStoreTests.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Storage/StoreWiringTests.cs`

**How the Table store works.** Each row keeps the whole record as JSON in one `Data` column. `PartitionKey` and `RowKey` are both the SHA-256 hex of the record key, because Table keys forbid characters such as `/ # ? \` and our keys contain caller-chosen IDs. Writes use `AddEntityAsync` (409 if the row exists) or `UpdateEntityAsync` with the read ETag and `TableUpdateMode.Replace` (412 if someone else wrote first). Times are UTC `DateTimeOffset` values from `TimeProvider`. Status values are strings inside the JSON, so the Table SDK's enum handling never applies. Inbox rows use the hashed username as `PartitionKey` and "max ticks minus created ticks" as `RowKey`, so a partition query returns the newest message first.

The Table Storage contract tests run only when Azurite is available (`VOICERESET_TEST_AZURITE=1`); otherwise they are reported as skipped. To run them: `npm install -g azurite`, then `azurite --silent --location $env:TEMP\azurite` in another terminal, then `$env:VOICERESET_TEST_AZURITE = "1"` before `dotnet test`.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Storage/TableMockStoreTests.cs`:

```csharp
using Azure.Data.Tables;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Storage;

/// <summary>Runs the same store contract against Azurite (local Table Storage emulator).</summary>
public sealed class TableMockStoreTests : MockStoreContract
{
    protected override async Task<IMockStore> CreateStoreAsync()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("VOICERESET_TEST_AZURITE") == "1",
            "Start Azurite and set VOICERESET_TEST_AZURITE=1 to run the Table Storage tests.");
        TableMockStore store = new(new TableServiceClient("UseDevelopmentStorage=true"));
        await store.CreateTablesAsync(TestContext.Current.CancellationToken);
        return store;
    }
}
```

`tests/VoiceReset.Mocks.Tests/Storage/StoreWiringTests.cs`:

```csharp
namespace VoiceReset.Mocks.Tests.Storage;

public sealed class StoreWiringTests
{
    [Fact]
    public void Startup_NoTableEndpoint_LogsInMemoryStoreWarning()
    {
        using MocksFactory factory = new();

        factory.CreateClient();

        Assert.Contains(factory.Logs.Lines, line => line.Contains("[1008]", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Storage"`
Expected: build error `CS0246: The type or namespace name 'TableMockStore' could not be found`.

- [ ] **Step 3: Write the log messages**

All log messages of the app live here, so a reviewer can check in one file that no secret is ever logged.

`src/VoiceReset.Mocks/Shared/MockLog.cs`:

```csharp
namespace VoiceReset.Mocks.Shared;

/// <summary>
/// Every log message the mocks write (source-generated, no allocations when disabled).
/// Parameters are IDs, outcomes and status codes only: never codes, tokens, links,
/// passwords, usernames or request bodies.
/// </summary>
public static partial class MockLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Recovery {RecoveryId} started")]
    public static partial void RecoveryStarted(ILogger logger, string recoveryId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Verification for recovery {RecoveryId} answered {StatusCode}")]
    public static partial void VerificationAnswered(ILogger logger, string recoveryId, int statusCode);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Reset link issued for recovery {RecoveryId}")]
    public static partial void LinkIssued(ILogger logger, string recoveryId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Reset operation {OperationId} accepted for recovery {RecoveryId}")]
    public static partial void ResetAccepted(ILogger logger, string operationId, string recoveryId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Reset completed for recovery {RecoveryId}")]
    public static partial void ResetCompleted(ILogger logger, string recoveryId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Ticket {TicketId} outcome update {Outcome} (outcome changed: {Changed})")]
    public static partial void TicketOutcomeRecorded(ILogger logger, string ticketId, string outcome, bool changed);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "Mock inbox sign-in failed")]
    public static partial void InboxLoginFailed(ILogger logger);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "Storage:TableEndpoint is not set: using the in-memory store, data is lost on restart")]
    public static partial void UsingInMemoryStore(ILogger logger);
}
```

- [ ] **Step 4: Write the Table Storage store**

`src/VoiceReset.Mocks/Storage/TableMockStore.cs`:

```csharp
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Storage;

/// <summary>
/// Azure Table Storage. One row per record, the record as JSON in the "Data" column.
/// Keys are SHA-256 hex because Table keys forbid characters such as / # ? \.
/// </summary>
public sealed class TableMockStore(TableServiceClient tables) : IMockStore
{
    private const string DataColumn = "Data";

    public async Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        foreach (string name in StoreTables.All)
        {
            await tables.GetTableClient(name).CreateIfNotExistsAsync(cancellationToken);
        }
    }

    public async Task<T?> GetAsync<T>(string key) where T : StoredRecord
    {
        TableClient table = tables.GetTableClient(StoreTables.NameFor<T>());
        string rowKey = Secrets.Sha256Hex(key);
        NullableResponse<TableEntity> response = await table.GetEntityIfExistsAsync<TableEntity>(rowKey, rowKey);
        if (!response.HasValue || response.Value is null)
        {
            return null;
        }

        T? record = JsonSerializer.Deserialize<T>(response.Value.GetString(DataColumn) ?? "null", MockJson.Storage);
        if (record is not null)
        {
            record.ETag = response.Value.ETag.ToString();
        }

        return record;
    }

    public async Task<bool> TrySaveAsync<T>(T record) where T : StoredRecord
    {
        TableClient table = tables.GetTableClient(StoreTables.NameFor<T>());
        string rowKey = Secrets.Sha256Hex(record.GetKey());
        TableEntity entity = new(rowKey, rowKey)
        {
            [DataColumn] = JsonSerializer.Serialize(record, MockJson.Storage),
        };

        try
        {
            Response response = record.ETag is null
                ? await table.AddEntityAsync(entity)
                : await table.UpdateEntityAsync(entity, new ETag(record.ETag), TableUpdateMode.Replace);
            record.ETag = response.Headers.ETag?.ToString();
            return true;
        }
        catch (RequestFailedException error) when (error.Status is 404 or 409 or 412)
        {
            // 409: the row already exists. 412: someone else wrote first. 404: the row is gone.
            return false;
        }
    }

    public async Task AddInboxMessageAsync(InboxMessage message)
    {
        TableClient table = tables.GetTableClient(StoreTables.Inbox);
        string newestFirst = $"{DateTimeOffset.MaxValue.UtcTicks - message.CreatedAt.UtcTicks:D19}_{message.MessageId}";
        TableEntity entity = new(Secrets.Sha256Hex(message.Username), newestFirst)
        {
            [DataColumn] = JsonSerializer.Serialize(message, MockJson.Storage),
        };
        await table.AddEntityAsync(entity);
    }

    public async Task<IReadOnlyList<InboxMessage>> GetInboxAsync(string username, int maxMessages)
    {
        TableClient table = tables.GetTableClient(StoreTables.Inbox);
        string partitionKey = Secrets.Sha256Hex(username);
        List<InboxMessage> messages = [];
        await foreach (TableEntity entity in table.QueryAsync<TableEntity>(row => row.PartitionKey == partitionKey, maxPerPage: maxMessages))
        {
            InboxMessage? message = JsonSerializer.Deserialize<InboxMessage>(entity.GetString(DataColumn) ?? "null", MockJson.Storage);
            if (message is not null)
            {
                messages.Add(message);
            }

            if (messages.Count == maxMessages)
            {
                break;
            }
        }

        return messages;
    }
}
```

- [ ] **Step 5: Wire the store and the credential in `Program.cs`**

In `src/VoiceReset.Mocks/Program.cs`, add these `using` lines at the top (keep the existing ones):

```csharp
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using VoiceReset.Mocks.Storage;
```

Insert this block directly after the line `builder.Services.AddSingleton<SyntheticUsers>();`:

```csharp

// Storage: Table Storage when an endpoint is configured, otherwise in memory (data lost on restart).
string? tableEndpoint = builder.Configuration["Storage:TableEndpoint"];
if (string.IsNullOrWhiteSpace(tableEndpoint))
{
    builder.Services.AddSingleton<IMockStore, InMemoryMockStore>();
}
else
{
    // The one place that chooses the Azure credential: the app's own managed identity
    // in Azure, the developer's "az login" in Development. Never DefaultAzureCredential.
    TokenCredential credential = builder.Environment.IsDevelopment()
        ? new AzureCliCredential()
        : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
    builder.Services.AddAzureClients(clients =>
    {
        clients.AddTableServiceClient(new Uri(tableEndpoint));
        clients.UseCredential(credential);
    });
    builder.Services.AddSingleton<IMockStore, TableMockStore>();
}
```

Insert this block directly after the line `WebApplication app = builder.Build();`:

```csharp

if (app.Services.GetRequiredService<IMockStore>() is TableMockStore tableStore)
{
    await tableStore.CreateTablesAsync();
}
else
{
    MockLog.UsingInMemoryStore(app.Logger);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Storage"`
Expected: `Passed!`, `failed: 0`; the 7 `TableMockStoreTests` show as `skipped` unless Azurite is running.

- [ ] **Step 7: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Storage
git commit -m "feat(mocks): add table storage store and log messages"
```

---

### Task 6: Service authentication and `POST /v1/recoveries` (basic path)

**Files:**
- Create: `src/VoiceReset.Mocks/Shared/RequestRules.cs`
- Create: `src/VoiceReset.Mocks/Shared/ServiceAuth.cs`
- Create: `src/VoiceReset.Mocks/Issuer/IssuerRequests.cs`
- Create: `src/VoiceReset.Mocks/Issuer/ResetCompletion.cs`
- Create: `src/VoiceReset.Mocks/Issuer/RecoveryService.cs`
- Create: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Create: `tests/VoiceReset.Mocks.Tests/TestApi.cs`
- Create: `tests/VoiceReset.Mocks.Tests/TestJourney.cs`
- Create: `tests/VoiceReset.Mocks.Tests/ThrowingStore.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryTests.cs`

`ResetCompletion` is created here because `RecoveryService` needs it to decide whether an account still has an active recovery (a pending reset can complete when its time comes). Its completion behaviour is tested in Tasks 12 and 13.

- [ ] **Step 1: Write the test helpers**

`tests/VoiceReset.Mocks.Tests/TestApi.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests;

/// <summary>Small HTTP and JSON helpers. Bodies are sent exactly as written (snake_case names).</summary>
public static class TestApi
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body, string? idempotencyKey = null) =>
        client.PostRawAsync(url, JsonSerializer.Serialize(body), idempotencyKey: idempotencyKey);

    public static async Task<HttpResponseMessage> PostRawAsync(
        this HttpClient client, string url, string body, string contentType = "application/json", string? idempotencyKey = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, Ct);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    public static Task<string> ReadTextAsync(this HttpResponseMessage response) => response.Content.ReadAsStringAsync(Ct);

    public static string ErrorCode(this JsonElement json) => json.GetProperty("error").GetProperty("code").GetString() ?? "";

    public static string Text(this JsonElement json, string property) => json.GetProperty(property).GetString() ?? "";

    public static string[] PropertyNames(this JsonElement json) =>
        json.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
```

`tests/VoiceReset.Mocks.Tests/TestJourney.cs`:

```csharp
using System.Net;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests;

/// <summary>
/// The common steps of a recovery, so each test only spells out the step it checks.
/// Codes and tokens are read from the store, the same data the inbox page shows.
/// </summary>
public sealed class TestJourney(MocksFactory factory)
{
    public const string NewPassword = "Fresh-Synthetic-Pass-42";

    public HttpClient Service { get; } = factory.CreateServiceClient();

    public HttpClient Browser { get; } = factory.CreateClient();

    public static string NewKey() => Guid.NewGuid().ToString("N");

    public Task<HttpResponseMessage> StartRawAsync(string username, string? requestId = null) =>
        Service.PostJsonAsync("/v1/recoveries", new { username, request_id = requestId ?? NewKey() });

    public async Task<string> StartAsync(string username = "alice")
    {
        HttpResponseMessage response = await StartRawAsync(username);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.ReadJsonAsync()).Text("recovery_id");
    }

    public async Task<string> LatestCodeAsync(string username = "alice")
    {
        IReadOnlyList<InboxMessage> inbox = await factory.Store.GetInboxAsync(username, 50);
        return inbox.First(message => message.Kind == InboxMessageKinds.VerificationCode).Code ?? "";
    }

    public Task<HttpResponseMessage> VerifyAsync(string recoveryId, string code, string? idempotencyKey = null) =>
        Service.PostJsonAsync($"/v1/recoveries/{recoveryId}/verify", new { code }, idempotencyKey ?? NewKey());

    public async Task<string> StartAndVerifyAsync(string username = "alice")
    {
        string recoveryId = await StartAsync(username);
        HttpResponseMessage response = await VerifyAsync(recoveryId, await LatestCodeAsync(username));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return recoveryId;
    }

    public Task<HttpResponseMessage> IssueLinkAsync(string recoveryId, string? operationId = null) =>
        Service.PostJsonAsync($"/v1/recoveries/{recoveryId}/reset-link", new { operation_id = operationId ?? NewKey() });

    public async Task<string> LatestTokenAsync(string username = "alice")
    {
        IReadOnlyList<InboxMessage> inbox = await factory.Store.GetInboxAsync(username, 50);
        string link = inbox.First(message => message.Kind == InboxMessageKinds.ResetLink).Link ?? "";
        const string marker = "#token=";
        return Uri.UnescapeDataString(link[(link.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
    }

    public async Task<(string RecoveryId, string Token)> ReadyForResetAsync(string username = "alice")
    {
        string recoveryId = await StartAndVerifyAsync(username);
        HttpResponseMessage response = await IssueLinkAsync(recoveryId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (recoveryId, await LatestTokenAsync(username));
    }

    public Task<HttpResponseMessage> ValidateAsync(string token, string password) =>
        Browser.PostJsonAsync("/v1/password/validate", new { token, password });

    public Task<HttpResponseMessage> ResetAsync(string token, string newPassword, string? operationId = null) =>
        Browser.PostJsonAsync("/v1/resets", new { token, new_password = newPassword, operation_id = operationId ?? NewKey() });

    public async Task<(string RecoveryId, string Receipt)> CompleteResetAsync(string username = "alice")
    {
        (string recoveryId, string token) = await ReadyForResetAsync(username);
        HttpResponseMessage response = await ResetAsync(token, NewPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (recoveryId, (await response.ReadJsonAsync()).Text("reset_receipt"));
    }

    public async Task<string> CreateTicketAsync(string recoveryId)
    {
        HttpResponseMessage response = await Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = NewKey() });
        return (await response.ReadJsonAsync()).Text("ticket_id");
    }

    public Task<HttpResponseMessage> RecordOutcomeAsync(
        string ticketId, string outcome, string reasonCode, string? receipt = null, string? operationId = null) =>
        Service.PostJsonAsync($"/v1/tickets/{ticketId}/outcome", new
        {
            outcome,
            reset_receipt = receipt,
            reason_code = reasonCode,
            operation_id = operationId ?? NewKey(),
        });
}
```

`tests/VoiceReset.Mocks.Tests/ThrowingStore.cs`:

```csharp
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests;

/// <summary>Hand-written fake: a store that is always down.</summary>
public sealed class ThrowingStore : IMockStore
{
    public Task<T?> GetAsync<T>(string key) where T : StoredRecord => throw Down();

    public Task<bool> TrySaveAsync<T>(T record) where T : StoredRecord => throw Down();

    public Task AddInboxMessageAsync(InboxMessage message) => throw Down();

    public Task<IReadOnlyList<InboxMessage>> GetInboxAsync(string username, int maxMessages) => throw Down();

    private static InvalidOperationException Down() => new("Storage is down (test fake).");
}
```

- [ ] **Step 2: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class StartRecoveryTests
{
    [Fact]
    public async Task StartRecovery_KnownUser_Returns202WithExactlyTheContractFields()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.StartRawAsync("alice");
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(new[] { "attempts_remaining", "recovery_id", "status", "verification_expires_at" }, json.PropertyNames());
        Assert.StartsWith("rec_", json.Text("recovery_id"));
        Assert.Equal("awaiting_verification", json.Text("status"));
        Assert.Equal(MocksFactory.StartTime.AddSeconds(120), json.GetProperty("verification_expires_at").GetDateTimeOffset());
        Assert.Equal(2, json.GetProperty("attempts_remaining").GetInt32());
    }

    [Fact]
    public async Task StartRecovery_KnownUser_DeliversSixDigitCodeToInboxButNotInResponse()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.StartRawAsync("alice");
        string body = await response.ReadTextAsync();
        IReadOnlyList<InboxMessage> inbox = await factory.Store.GetInboxAsync("alice", 50);

        InboxMessage message = Assert.Single(inbox);
        Assert.Equal(InboxMessageKinds.VerificationCode, message.Kind);
        Assert.Matches("^[0-9]{6}$", message.Code);
        Assert.DoesNotContain(message.Code ?? "missing", body);
    }

    [Fact]
    public async Task StartRecovery_NoAuthorizationHeader_Returns401Unauthenticated()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostJsonAsync("/v1/recoveries", new { username = "alice", request_id = "r1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task StartRecovery_WrongCredential_Returns401Unauthenticated()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-service-credential");

        HttpResponseMessage response = await client.PostJsonAsync("/v1/recoveries", new { username = "alice", request_id = "r1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"username":"alice"}""")]
    [InlineData("""{"username":"alice","request_id":"r1","extra":"x"}""")]
    [InlineData("""{"username":1,"request_id":"r1"}""")]
    [InlineData("""{"username":"alice","request_id":null}""")]
    [InlineData("""{"username":"   ","request_id":"r1"}""")]
    [InlineData("""{"username":"alice","request_id":""}""")]
    [InlineData("""not json""")]
    public async Task StartRecovery_InvalidBody_Returns400InvalidRequest(string body)
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateServiceClient();

        HttpResponseMessage response = await client.PostRawAsync("/v1/recoveries", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task StartRecovery_NotJsonContentType_Returns400InvalidRequest()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateServiceClient();

        HttpResponseMessage response = await client.PostRawAsync("/v1/recoveries", """{"username":"alice","request_id":"r1"}""", "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartRecovery_StoreDown_Returns503DependencyUnavailable()
    {
        using MocksFactory factory = new(store: new ThrowingStore());
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.StartRawAsync("alice");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("dependency_unavailable", (await response.ReadJsonAsync()).ErrorCode());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.StartRecoveryTests"`
Expected: `failed: 13` (the route does not exist yet, so the app answers 404 with an empty body).

- [ ] **Step 4: Write the shared request rules and service authentication**

`src/VoiceReset.Mocks/Shared/RequestRules.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace VoiceReset.Mocks.Shared;

/// <summary>Field checks shared by the routes. Failing one means 400 invalid_request.</summary>
public static class RequestRules
{
    public const int MaxIdLength = 128;
    public const int MaxUsernameLength = 128;

    /// <summary>Opaque IDs and idempotency keys: 1 to 128 characters, no control characters.</summary>
    public static bool IsValidId([NotNullWhen(true)] string? value) =>
        value is { Length: > 0 and <= MaxIdLength } && !value.Any(char.IsControl);

    /// <summary>Any non-blank username is accepted (unknown ones get a decoy), but not an endless one.</summary>
    public static bool IsValidUsername(string value) =>
        value.Trim().Length is > 0 and <= MaxUsernameLength && !value.Any(char.IsControl);
}
```

`src/VoiceReset.Mocks/Shared/ServiceAuth.cs`:

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Configuration;

namespace VoiceReset.Mocks.Shared;

/// <summary>Checks "Authorization: Bearer &lt;Mocks:ServiceCredential&gt;" in constant time. One namespace.</summary>
public sealed class ServiceAuth(IOptions<MocksOptions> options)
{
    private const string Prefix = "Bearer ";

    public bool IsServiceRequest(HttpRequest request)
    {
        string header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return Secrets.FixedTimeEquals(
            Secrets.Sha256Hex(header[Prefix.Length..]),
            Secrets.Sha256Hex(options.Value.ServiceCredential));
    }
}

/// <summary>Endpoint filter for service-only routes: answers 401 before the body is read.</summary>
public sealed class ServiceAuthFilter(ServiceAuth auth) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        auth.IsServiceRequest(context.HttpContext.Request) ? await next(context) : ApiResult.Unauthenticated();
}
```

- [ ] **Step 5: Write the request records**

`src/VoiceReset.Mocks/Issuer/IssuerRequests.cs`:

```csharp
namespace VoiceReset.Mocks.Issuer;

// Request bodies of the issuer routes. MockJson.Requests maps them strictly
// (snake_case, every field required, no extra fields, no wrong types).

public sealed record StartRecoveryRequest(string Username, string RequestId);

public sealed record VerifyRequest(string Code);

public sealed record ResetLinkRequest(string OperationId);

public sealed record ValidatePasswordRequest(string Token, string Password);

public sealed record ResetRequest(string Token, string NewPassword, string OperationId);
```

- [ ] **Step 6: Write `ResetCompletion`**

`src/VoiceReset.Mocks/Issuer/ResetCompletion.cs`:

```csharp
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Issuer;

/// <summary>
/// Loads a recovery and, when an accepted reset is due, completes it first.
/// Completion is checked lazily on every read (no background job): a pending
/// reset becomes "completed" the first time anyone looks after its time.
/// </summary>
public sealed class ResetCompletion(IMockStore store, SyntheticUsers users, TimeProvider time, ILogger<ResetCompletion> logger)
{
    public async Task<RecoveryRecord?> LoadAsync(string recoveryId)
    {
        for (int attempt = 0; attempt < StoreRetry.MaxAttempts; attempt++)
        {
            RecoveryRecord? recovery = await store.GetAsync<RecoveryRecord>(recoveryId);
            if (recovery is null || recovery.Status != RecoveryStatus.ResetPending || time.GetUtcNow() < recovery.ResetCompletesAt)
            {
                return recovery;
            }

            // The new password (and unlock) commit first; only then does the recovery get a receipt.
            await SaveNewPasswordAsync(recovery);
            recovery.Status = RecoveryStatus.Completed;
            recovery.ResetReceipt = Secrets.NewId("rcpt");
            recovery.UnlockStatus = users.Find(recovery.Username)?.RequiresUnlock == true
                ? UnlockStatuses.Unlocked
                : UnlockStatuses.NotRequired;
            if (await store.TrySaveAsync(recovery))
            {
                MockLog.ResetCompleted(logger, recovery.RecoveryId);
                return recovery;
            }
        }

        throw new InvalidOperationException("The recovery kept changing while a reset was being completed.");
    }

    private async Task SaveNewPasswordAsync(RecoveryRecord recovery)
    {
        for (int attempt = 0; attempt < StoreRetry.MaxAttempts; attempt++)
        {
            AccountRecord account = await store.GetAsync<AccountRecord>(recovery.Username)
                ?? new AccountRecord { Username = recovery.Username };
            account.PasswordHash = recovery.NewPasswordHash;
            if (await store.TrySaveAsync(account))
            {
                return;
            }
        }

        throw new InvalidOperationException("The account kept changing while a new password was being saved.");
    }
}
```

- [ ] **Step 7: Write `RecoveryService`**

`src/VoiceReset.Mocks/Issuer/RecoveryService.cs`:

```csharp
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Issuer;

/// <summary>Starts recoveries (with decoys and throttling) and reports recovery status.</summary>
public sealed class RecoveryService(
    IMockStore store,
    SyntheticUsers users,
    ResetCompletion completion,
    TimeProvider time,
    ILogger<RecoveryService> logger)
{
    public const int AttemptBudget = 2;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(120);
    private const string StartAction = "start";

    /// <summary>The stored form of a code, bound to its recovery: a code from another recovery never matches.</summary>
    public static string CodeHash(string recoveryId, string code) => Secrets.Sha256Hex($"{recoveryId}\n{code}");

    public async Task<ApiResult> StartAsync(StartRecoveryRequest request)
    {
        if (!RequestRules.IsValidUsername(request.Username) || !RequestRules.IsValidId(request.RequestId))
        {
            return ApiResult.InvalidRequest();
        }

        string username = SyntheticUsers.Normalize(request.Username);
        string fingerprint = Secrets.Sha256Hex(username);

        // A retry with the same request_id returns the original result: no new code, no new expiry.
        IdempotencyRecord? previous = await store.GetAsync<IdempotencyRecord>(IdempotencyRecord.KeyFor(StartAction, request.RequestId));
        if (previous is not null)
        {
            return previous.Fingerprint == fingerprint
                ? new ApiResult(previous.StatusCode, previous.Json)
                : ApiResult.IdempotencyConflict();
        }

        DateTimeOffset now = time.GetUtcNow();

        // Known and unknown usernames both get an account row, so throttling is the same for decoys.
        AccountRecord account = await store.GetAsync<AccountRecord>(username) ?? new AccountRecord { Username = username };
        if (await RetryAfterSecondsAsync(account, now) is int waitSeconds)
        {
            return ApiResult.Throttled(waitSeconds);
        }

        MockUserOptions? user = users.Find(username);
        string code = Secrets.NewCode();
        RecoveryRecord recovery = new()
        {
            RecoveryId = Secrets.NewId("rec"),
            Username = username,
            IsDecoy = user is null,
            VerificationExpiresAt = now + CodeLifetime,
            AttemptsRemaining = AttemptBudget,
        };
        recovery.CodeHash = user is null ? null : CodeHash(recovery.RecoveryId, code);

        // Claim the account first. If a parallel start for the same account won, throttle this one.
        account.ActiveRecoveryId = recovery.RecoveryId;
        account.LastCodeIssuedAt = now;
        if (!await store.TrySaveAsync(account))
        {
            return ApiResult.Throttled((int)CodeLifetime.TotalSeconds);
        }

        await store.TrySaveAsync(recovery); // a fresh random ID: the insert cannot clash

        if (user is not null)
        {
            await store.AddInboxMessageAsync(new InboxMessage(
                username, Secrets.NewId("msg"), now, InboxMessageKinds.VerificationCode, code, null, recovery.VerificationExpiresAt));
        }

        ApiResult result = ApiResult.Of(StatusCodes.Status202Accepted, new
        {
            recovery.RecoveryId,
            recovery.Status,
            recovery.VerificationExpiresAt,
            recovery.AttemptsRemaining,
        });
        await store.TrySaveAsync(new IdempotencyRecord
        {
            Action = StartAction,
            IdempotencyKey = request.RequestId,
            Fingerprint = fingerprint,
            RecoveryId = recovery.RecoveryId,
            StatusCode = result.StatusCode,
            Json = result.Json,
        });
        MockLog.RecoveryStarted(logger, recovery.RecoveryId);
        return result;
    }

    public async Task<ApiResult> GetAsync(string recoveryId)
    {
        RecoveryRecord? recovery = await completion.LoadAsync(recoveryId);
        if (recovery is null)
        {
            return ApiResult.NotFound();
        }

        return ApiResult.Ok(new
        {
            recovery.RecoveryId,
            Status = recovery.StatusAt(time.GetUtcNow()),
            recovery.VerificationExpiresAt,
            recovery.AttemptsRemaining,
            recovery.LinkExpiresAt,
            recovery.ResetOperationId,
            recovery.ResetReceipt,
            recovery.UnlockStatus,
        });
    }

    /// <summary>
    /// Null when a new start is allowed. Otherwise the seconds to wait: while the
    /// account has an active recovery, and at most one new code per 120 seconds.
    /// </summary>
    private async Task<int?> RetryAfterSecondsAsync(AccountRecord account, DateTimeOffset now)
    {
        DateTimeOffset? blockedUntil = account.LastCodeIssuedAt + CodeLifetime;
        if (account.ActiveRecoveryId is not null
            && await completion.LoadAsync(account.ActiveRecoveryId) is { } active
            && active.ActiveUntil() is { } activeUntil
            && (blockedUntil is null || activeUntil > blockedUntil))
        {
            blockedUntil = activeUntil;
        }

        return blockedUntil > now ? Math.Max(1, (int)Math.Ceiling((blockedUntil.Value - now).TotalSeconds)) : null;
    }
}
```

- [ ] **Step 8: Write the endpoints**

`src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`:

```csharp
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Issuer;

/// <summary>The /v1 issuer routes. Handlers only read the request and call a service.</summary>
public static class IssuerEndpoints
{
    public const string ResetFormCorsPolicy = "reset-form";

    public static void MapIssuerEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder service = app.MapGroup("/v1")
            .AddEndpointFilter<ServiceAuthFilter>();

        service.MapPost("/recoveries", StartRecoveryAsync);
    }

    private static async Task<ApiResult> StartRecoveryAsync(HttpRequest http, RecoveryService recoveries) =>
        await MockJson.ReadAsync<StartRecoveryRequest>(http) is { } request
            ? await recoveries.StartAsync(request)
            : ApiResult.InvalidRequest();
}
```

- [ ] **Step 9: Register the services and map the routes**

In `src/VoiceReset.Mocks/Program.cs`, add `using VoiceReset.Mocks.Issuer;` to the `using` lines.

Insert this block directly before the line `WebApplication app = builder.Build();`:

```csharp
// Issuer and tickets (stateless singletons; all state is in the store)
builder.Services.AddSingleton<ServiceAuth>();
builder.Services.AddSingleton<ResetCompletion>();
builder.Services.AddSingleton<RecoveryService>();

```

Insert this line directly after `app.MapHealthEndpoints();`:

```csharp
app.MapIssuerEndpoints();
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.StartRecoveryTests"`
Expected: `Passed!`, `succeeded: 13`, `failed: 0`.

- [ ] **Step 11: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests
git commit -m "feat(mocks): add service auth and recovery start endpoint"
```

---

### Task 7: Start rules: idempotency, throttling, decoys, restarts

**Files:**
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryRulesTests.cs`
- (Code under test: `src/VoiceReset.Mocks/Issuer/RecoveryService.cs` from Task 6)

The start rules live in the same method as the start route (Task 6), so these tests are expected to pass at once. Step 3 proves they really guard the rules by breaking the code on purpose.

- [ ] **Step 1: Write the tests**

`tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryRulesTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class StartRecoveryRulesTests
{
    [Fact]
    public async Task StartRecovery_SameRequestIdRetried_ReturnsOriginalResultWithoutNewCode()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        HttpResponseMessage first = await journey.StartRawAsync("alice", "request-1");
        factory.Time.Advance(TimeSpan.FromSeconds(5));

        HttpResponseMessage retry = await journey.StartRawAsync("alice", "request-1");

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
        Assert.Single(await factory.Store.GetInboxAsync("alice", 50));
    }

    [Fact]
    public async Task StartRecovery_SameRequestIdDifferentUsername_Returns409IdempotencyConflict()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.StartRawAsync("alice", "request-1");

        HttpResponseMessage response = await journey.StartRawAsync("bob", "request-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_conflict", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task StartRecovery_DifferentRequestWhileActive_Returns429WithRetryAfter()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.StartAsync("alice");
        factory.Time.Advance(TimeSpan.FromSeconds(30));

        HttpResponseMessage response = await journey.StartRawAsync("alice");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal("throttled", (await response.ReadJsonAsync()).ErrorCode());
        Assert.Equal(TimeSpan.FromSeconds(90), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task StartRecovery_AfterVerificationWindow_IssuesNewCode()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.StartAsync("alice");
        factory.Time.Advance(TimeSpan.FromSeconds(120));

        HttpResponseMessage response = await journey.StartRawAsync("alice");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, (await factory.Store.GetInboxAsync("alice", 50)).Count);
    }

    [Fact]
    public async Task StartRecovery_UsernameWithOtherCaseAndSpaces_IsTheSameAccount()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.StartAsync("alice");

        HttpResponseMessage response = await journey.StartRawAsync("  ALICE ");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact]
    public async Task StartRecovery_UnknownUsername_ReturnsSameEnvelopeAndDeliversNothing()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage knownResponse = await journey.StartRawAsync("alice");
        HttpResponseMessage decoyResponse = await journey.StartRawAsync("nobody.here");
        JsonElement known = await knownResponse.ReadJsonAsync();
        JsonElement decoy = await decoyResponse.ReadJsonAsync();

        Assert.Equal(knownResponse.StatusCode, decoyResponse.StatusCode);
        Assert.Equal(known.PropertyNames(), decoy.PropertyNames());
        Assert.Equal(known.Text("status"), decoy.Text("status"));
        Assert.Equal(known.GetProperty("verification_expires_at").GetDateTimeOffset(), decoy.GetProperty("verification_expires_at").GetDateTimeOffset());
        Assert.Equal(2, decoy.GetProperty("attempts_remaining").GetInt32());
        Assert.Empty(await factory.Store.GetInboxAsync("nobody.here", 50));
    }

    [Fact]
    public async Task StartRecovery_UnknownUsernameWhileActive_ThrottledLikeKnownUsername()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.StartAsync("alice");
        await journey.StartAsync("nobody.here");

        HttpResponseMessage known = await journey.StartRawAsync("alice");
        HttpResponseMessage decoy = await journey.StartRawAsync("nobody.here");

        Assert.Equal((HttpStatusCode)429, decoy.StatusCode);
        Assert.Equal(known.Headers.RetryAfter?.Delta, decoy.Headers.RetryAfter?.Delta);
        Assert.Equal(await known.ReadTextAsync(), await decoy.ReadTextAsync());
    }

    [Fact]
    public async Task StartRecovery_AfterRestartOnSameStore_StillThrottled()
    {
        InMemoryMockStore sharedStore = new();
        using (MocksFactory beforeRestart = new(sharedStore))
        {
            await new TestJourney(beforeRestart).StartAsync("alice");
        }

        using MocksFactory afterRestart = new(sharedStore);
        HttpResponseMessage response = await new TestJourney(afterRestart).StartRawAsync("alice");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }

    [Fact]
    public async Task StartRecovery_TwoParallelStartsForSameAccount_OnlyOneAccepted()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage[] responses = await Task.WhenAll(journey.StartRawAsync("alice"), journey.StartRawAsync("alice"));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(responses, response => response.StatusCode == (HttpStatusCode)429);
        Assert.Single(await factory.Store.GetInboxAsync("alice", 50));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.StartRecoveryRulesTests"`
Expected: `Passed!`, `succeeded: 9`, `failed: 0`.

- [ ] **Step 3: Prove the tests guard the rules**

In `RecoveryService.StartAsync`, temporarily replace `if (await RetryAfterSecondsAsync(account, now) is int waitSeconds)` with `if (false && await RetryAfterSecondsAsync(account, now) is int waitSeconds)` and run the same command.
Expected: `failed: 5` or more (the throttling, decoy-throttling, restart and same-account tests). Then **undo the change** and run again: `failed: 0`.

- [ ] **Step 4: Commit**

```powershell
git add tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryRulesTests.cs
git commit -m "test(mocks): cover recovery start idempotency, throttling and decoys"
```

---

### Task 8: `POST /v1/recoveries/{id}/verify`

**Files:**
- Create: `src/VoiceReset.Mocks/Issuer/VerificationService.cs`
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/VerifyTests.cs`

**Rules implemented here.** The code is valid for 120 seconds from issuance (`now >= deadline` is expired). Two completed wrong submissions exhaust the recovery; it stays exhausted. The `Idempotency-Key` result is saved **in the same row** as the attempt counter, so a retry can never count twice, even under concurrency. Same key with a different code → `409 idempotency_conflict`. After verification, a new key → `409 invalid_state`; the original key replays its `200`. Decoys have no code, so every submission is wrong in exactly the same way. Malformed JSON is rejected before anything is counted.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/VerifyTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class VerifyTests
{
    [Fact]
    public async Task Verify_CorrectCode_Returns200VerifiedWithContractFields()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, await journey.LatestCodeAsync());
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "attempts_remaining", "recovery_id", "status" }, json.PropertyNames());
        Assert.Equal(recoveryId, json.Text("recovery_id"));
        Assert.Equal("verified", json.Text("status"));
        Assert.Equal(2, json.GetProperty("attempts_remaining").GetInt32());
    }

    [Fact]
    public async Task Verify_FirstWrongCode_Returns422WithOneAttemptLeftAndNoEcho()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string wrongCode = WrongCode(await journey.LatestCodeAsync());

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, wrongCode);
        string body = await response.ReadTextAsync();
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("verification_failed", json.ErrorCode());
        Assert.Equal(1, json.GetProperty("attempts_remaining").GetInt32());
        Assert.Equal("awaiting_verification", json.Text("status"));
        Assert.DoesNotContain(wrongCode, body);
    }

    [Fact]
    public async Task Verify_SecondWrongCode_Returns409ExhaustedWithZeroAttempts()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string wrongCode = WrongCode(await journey.LatestCodeAsync());
        await journey.VerifyAsync(recoveryId, wrongCode);

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, wrongCode);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("verification_exhausted", json.ErrorCode());
        Assert.Equal(0, json.GetProperty("attempts_remaining").GetInt32());
        Assert.Equal("exhausted", json.Text("status"));
    }

    [Fact]
    public async Task Verify_SameWrongCodeTwiceWithDifferentKeys_CountsAsTwoAttempts()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string wrongCode = WrongCode(await journey.LatestCodeAsync());
        await journey.VerifyAsync(recoveryId, wrongCode);

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, wrongCode);

        Assert.Equal("verification_exhausted", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Verify_CorrectCodeAfterExhaustion_StaysExhausted()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string code = await journey.LatestCodeAsync();
        await journey.VerifyAsync(recoveryId, WrongCode(code));
        await journey.VerifyAsync(recoveryId, WrongCode(code));

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, code);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("verification_exhausted", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Verify_SameKeyRetried_ReplaysResultWithoutCountingAgain()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string code = await journey.LatestCodeAsync();
        HttpResponseMessage first = await journey.VerifyAsync(recoveryId, WrongCode(code), "key-1");

        HttpResponseMessage retry = await journey.VerifyAsync(recoveryId, WrongCode(code), "key-1");
        HttpResponseMessage correct = await journey.VerifyAsync(recoveryId, code, "key-2");

        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, retry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Equal(1, (await correct.ReadJsonAsync()).GetProperty("attempts_remaining").GetInt32());
    }

    [Fact]
    public async Task Verify_SameKeyDifferentCode_Returns409IdempotencyConflict()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        await journey.VerifyAsync(recoveryId, "000000", "key-1");

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, "111111", "key-1");

        Assert.Equal("idempotency_conflict", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Verify_CorrectCodeAt119Seconds_Verifies()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        factory.Time.Advance(TimeSpan.FromSeconds(119));

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, await journey.LatestCodeAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Verify_CorrectCodeAt120Seconds_Returns410RecoveryExpired()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        factory.Time.Advance(TimeSpan.FromSeconds(120));

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, await journey.LatestCodeAsync());
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("recovery_expired", json.ErrorCode());
        Assert.Equal("expired", json.Text("status"));
    }

    [Fact]
    public async Task Verify_NewKeyAfterVerified_Returns409InvalidState()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();

        HttpResponseMessage response = await journey.VerifyAsync(recoveryId, "000000");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Verify_OriginalKeyAfterVerified_ReplaysOriginal200()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string code = await journey.LatestCodeAsync();
        HttpResponseMessage first = await journey.VerifyAsync(recoveryId, code, "key-1");

        HttpResponseMessage retry = await journey.VerifyAsync(recoveryId, code, "key-1");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
    }

    [Fact]
    public async Task Verify_DecoyRecovery_FailsExactlyLikeWrongCodesForKnownUser()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string knownId = await journey.StartAsync("alice");
        string decoyId = await journey.StartAsync("nobody.here");

        HttpResponseMessage knownFirst = await journey.VerifyAsync(knownId, WrongCode(await journey.LatestCodeAsync()));
        HttpResponseMessage decoyFirst = await journey.VerifyAsync(decoyId, "123456");
        HttpResponseMessage knownSecond = await journey.VerifyAsync(knownId, WrongCode(await journey.LatestCodeAsync()));
        HttpResponseMessage decoySecond = await journey.VerifyAsync(decoyId, "123456");

        Assert.Equal(knownFirst.StatusCode, decoyFirst.StatusCode);
        Assert.Equal(await knownFirst.ReadTextAsync(), await decoyFirst.ReadTextAsync());
        Assert.Equal(knownSecond.StatusCode, decoySecond.StatusCode);
        Assert.Equal(await knownSecond.ReadTextAsync(), await decoySecond.ReadTextAsync());
    }

    [Fact]
    public async Task Verify_CodeFromAnotherRecovery_Returns422()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string aliceRecovery = await journey.StartAsync("alice");
        await journey.StartAsync("bob");

        HttpResponseMessage response = await journey.VerifyAsync(aliceRecovery, await journey.LatestCodeAsync("bob"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Verify_MissingIdempotencyKey_Returns400InvalidRequest()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.Service.PostJsonAsync($"/v1/recoveries/{recoveryId}/verify", new { code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Verify_MalformedJson_IsNotCountedAsAnAttempt()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        HttpResponseMessage malformed = await journey.Service.PostRawAsync($"/v1/recoveries/{recoveryId}/verify", "{\"code\":", idempotencyKey: "key-1");

        HttpResponseMessage wrong = await journey.VerifyAsync(recoveryId, WrongCode(await journey.LatestCodeAsync()));

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(1, (await wrong.ReadJsonAsync()).GetProperty("attempts_remaining").GetInt32());
    }

    [Fact]
    public async Task Verify_UnknownRecovery_Returns404NotFound()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.VerifyAsync("rec_doesnotexist", "000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Verify_AfterRestartOnSameStore_KeepsAttemptCounter()
    {
        InMemoryMockStore sharedStore = new();
        string recoveryId;
        using (MocksFactory beforeRestart = new(sharedStore))
        {
            TestJourney journey = new(beforeRestart);
            recoveryId = await journey.StartAsync();
            await journey.VerifyAsync(recoveryId, WrongCode(await journey.LatestCodeAsync()));
        }

        using MocksFactory afterRestart = new(sharedStore);
        HttpResponseMessage response = await new TestJourney(afterRestart).VerifyAsync(recoveryId, "999999x");

        Assert.Equal("verification_exhausted", (await response.ReadJsonAsync()).ErrorCode());
    }

    private static string WrongCode(string realCode) => realCode == "000000" ? "111111" : "000000";
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.VerifyTests"`
Expected: `failed: 17` (route missing → 404 with an empty body; `Verify_UnknownRecovery_Returns404NotFound` also fails because the body is not the JSON envelope).

- [ ] **Step 3: Write `VerificationService`**

`src/VoiceReset.Mocks/Issuer/VerificationService.cs`:

```csharp
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Issuer;

/// <summary>
/// Checks a submitted code. The result of each Idempotency-Key is saved in the
/// recovery row together with the attempt counter (one atomic ETag write), so a
/// retried submission is answered from the record and never counted twice.
/// </summary>
public sealed class VerificationService(IMockStore store, TimeProvider time, ILogger<VerificationService> logger)
{
    private const int MaxCodeLength = 32;

    public async Task<ApiResult> VerifyAsync(string recoveryId, string idempotencyKey, VerifyRequest request)
    {
        if (!RequestRules.IsValidId(idempotencyKey) || request.Code.Length is 0 or > MaxCodeLength)
        {
            return ApiResult.InvalidRequest();
        }

        string fingerprint = RecoveryService.CodeHash(recoveryId, request.Code);
        for (int attempt = 0; attempt < StoreRetry.MaxAttempts; attempt++)
        {
            RecoveryRecord? recovery = await store.GetAsync<RecoveryRecord>(recoveryId);
            if (recovery is null)
            {
                return ApiResult.NotFound();
            }

            if (recovery.VerifyResults.TryGetValue(idempotencyKey, out StoredResponse? previous))
            {
                return previous.Fingerprint == fingerprint
                    ? new ApiResult(previous.StatusCode, previous.Json)
                    : ApiResult.IdempotencyConflict();
            }

            ApiResult result = Evaluate(recovery, fingerprint, time.GetUtcNow());
            recovery.VerifyResults[idempotencyKey] = new StoredResponse(fingerprint, result.StatusCode, result.Json);
            if (await store.TrySaveAsync(recovery))
            {
                MockLog.VerificationAnswered(logger, recoveryId, result.StatusCode);
                return result;
            }
        }

        return ApiResult.DependencyUnavailable();
    }

    /// <summary>Decides the answer and updates the recovery (status, counter, consumed code).</summary>
    private static ApiResult Evaluate(RecoveryRecord recovery, string submittedHash, DateTimeOffset now)
    {
        if (recovery.Status == RecoveryStatus.Exhausted)
        {
            return Exhausted();
        }

        if (recovery.Status != RecoveryStatus.AwaitingVerification)
        {
            return ApiResult.InvalidState("This recovery is not waiting for a code.");
        }

        if (now >= recovery.VerificationExpiresAt)
        {
            return ApiResult.VerificationError(
                StatusCodes.Status410Gone, "recovery_expired", "The verification code has expired.",
                recovery.AttemptsRemaining, RecoveryStatus.Expired);
        }

        // Decoys have no code hash, so they always take the "wrong code" path below.
        if (recovery.CodeHash is not null && Secrets.FixedTimeEquals(recovery.CodeHash, submittedHash))
        {
            recovery.Status = RecoveryStatus.Verified;
            recovery.CodeHash = null; // consumed: it verified this recovery only
            return ApiResult.Ok(new { recovery.RecoveryId, recovery.Status, recovery.AttemptsRemaining });
        }

        recovery.AttemptsRemaining--;
        if (recovery.AttemptsRemaining > 0)
        {
            return ApiResult.VerificationError(
                StatusCodes.Status422UnprocessableEntity, "verification_failed", "The verification code is not correct.",
                recovery.AttemptsRemaining, RecoveryStatus.AwaitingVerification);
        }

        recovery.Status = RecoveryStatus.Exhausted;
        recovery.CodeHash = null;
        return Exhausted();
    }

    private static ApiResult Exhausted() =>
        ApiResult.VerificationError(
            StatusCodes.Status409Conflict, "verification_exhausted", "No verification attempts are left.",
            0, RecoveryStatus.Exhausted);
}
```

- [ ] **Step 4: Add the route and register the service**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, add this line after `service.MapPost("/recoveries", StartRecoveryAsync);`:

```csharp
        service.MapPost("/recoveries/{id}/verify", VerifyAsync);
```

and add this handler after `StartRecoveryAsync`:

```csharp

    private static async Task<ApiResult> VerifyAsync(string id, HttpRequest http, VerificationService verification) =>
        await MockJson.ReadAsync<VerifyRequest>(http) is { } request
            ? await verification.VerifyAsync(id, http.Headers["Idempotency-Key"].ToString(), request)
            : ApiResult.InvalidRequest();
```

In `src/VoiceReset.Mocks/Program.cs`, add after `builder.Services.AddSingleton<RecoveryService>();`:

```csharp
builder.Services.AddSingleton<VerificationService>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.VerifyTests"`
Expected: `Passed!`, `succeeded: 17`, `failed: 0`.

- [ ] **Step 6: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Issuer/VerifyTests.cs
git commit -m "feat(mocks): add code verification with idempotency and expiry"
```

---

### Task 9: `GET /v1/recoveries/{id}`

**Files:**
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/RecoveryStatusTests.cs`
- (Code under test: `RecoveryService.GetAsync` from Task 6)

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/RecoveryStatusTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class RecoveryStatusTests
{
    [Fact]
    public async Task GetRecovery_AfterStart_ReturnsAllContractFieldsWithNulls()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new[]
            {
                "attempts_remaining", "link_expires_at", "recovery_id", "reset_operation_id",
                "reset_receipt", "status", "unlock_status", "verification_expires_at",
            },
            json.PropertyNames());
        Assert.Equal("awaiting_verification", json.Text("status"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("link_expires_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reset_operation_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reset_receipt").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("unlock_status").ValueKind);
    }

    [Fact]
    public async Task GetRecovery_DeadlinePassedWithoutVerification_ReportsExpired()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        factory.Time.Advance(TimeSpan.FromSeconds(120));

        HttpResponseMessage response = await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct);

        Assert.Equal("expired", (await response.ReadJsonAsync()).Text("status"));
    }

    [Fact]
    public async Task GetRecovery_AfterTwoWrongCodes_ReportsExhausted()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync("nobody.here");
        await journey.VerifyAsync(recoveryId, "000000");
        await journey.VerifyAsync(recoveryId, "000000");

        HttpResponseMessage response = await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal("exhausted", json.Text("status"));
        Assert.Equal(0, json.GetProperty("attempts_remaining").GetInt32());
    }

    [Fact]
    public async Task GetRecovery_Verified_ReportsVerifiedWithoutTheCode()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();

        HttpResponseMessage response = await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct);
        string body = await response.ReadTextAsync();

        Assert.Equal("verified", (await response.ReadJsonAsync()).Text("status"));
        Assert.DoesNotContain(await journey.LatestCodeAsync(), body);
    }

    [Fact]
    public async Task GetRecovery_UnknownId_Returns404NotFound()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.Service.GetAsync("/v1/recoveries/rec_doesnotexist", TestApi.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task GetRecovery_NoAuthorization_Returns401Unauthenticated()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.Browser.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.RecoveryStatusTests"`
Expected: `failed: 6` (the GET route does not exist: 405 Method Not Allowed or 404).

- [ ] **Step 3: Add the route**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, add after the verify route line:

```csharp
        service.MapGet("/recoveries/{id}", GetRecoveryAsync);
```

and add this handler after `VerifyAsync`:

```csharp

    private static Task<ApiResult> GetRecoveryAsync(string id, RecoveryService recoveries) => recoveries.GetAsync(id);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.RecoveryStatusTests"`
Expected: `Passed!`, `succeeded: 6`, `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs tests/VoiceReset.Mocks.Tests/Issuer/RecoveryStatusTests.cs
git commit -m "feat(mocks): add recovery status endpoint"
```

---

### Task 10: `POST /v1/recoveries/{id}/reset-link`

**Files:**
- Create: `src/VoiceReset.Mocks/Issuer/ResetLinkService.cs`
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/ResetLinkTests.cs`

**Rules implemented here.** Only a verified, unexpired recovery gets a link. The token is 32 random bytes (256 bits) in base64url; only its SHA-256 is stored (in the recovery and in a `TokenRecord` for lookup). It expires 10 minutes after issuance. The link `{ResetBaseUrl}#token={URL-encoded token}` goes to the inbox only; the service gets `{recovery_id, status, link_expires_at}`. The `operation_id` is saved in the recovery row: the same operation replays the original result (no second delivery, same expiry); a different operation gets `409 invalid_state`.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/ResetLinkTests.cs`:

```csharp
using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class ResetLinkTests
{
    [Fact]
    public async Task IssueLink_VerifiedRecovery_Returns200WithTenMinuteExpiry()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();

        HttpResponseMessage response = await journey.IssueLinkAsync(recoveryId);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "link_expires_at", "recovery_id", "status" }, json.PropertyNames());
        Assert.Equal("link_issued", json.Text("status"));
        Assert.Equal(MocksFactory.StartTime.AddMinutes(10), json.GetProperty("link_expires_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task IssueLink_VerifiedRecovery_DeliversTokenLinkToInboxOnly()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();

        HttpResponseMessage response = await journey.IssueLinkAsync(recoveryId);
        string body = await response.ReadTextAsync();
        InboxMessage message = Assert.Single(await factory.Store.GetInboxAsync("alice", 50), m => m.Kind == InboxMessageKinds.ResetLink);
        string token = await journey.LatestTokenAsync();

        Assert.StartsWith(MocksFactory.ResetBaseUrl + "#token=", message.Link);
        Assert.Equal(32, Base64Url.DecodeFromChars(token).Length);
        Assert.DoesNotContain(token, body);
        Assert.DoesNotContain("token", body);
    }

    [Fact]
    public async Task IssueLink_SameOperationRetried_ReturnsOriginalResultWithoutSecondDelivery()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();
        HttpResponseMessage first = await journey.IssueLinkAsync(recoveryId, "link-op-1");
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        HttpResponseMessage retry = await journey.IssueLinkAsync(recoveryId, "link-op-1");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
        Assert.Single(await factory.Store.GetInboxAsync("alice", 50), m => m.Kind == InboxMessageKinds.ResetLink);
    }

    [Fact]
    public async Task IssueLink_DifferentOperationAfterIssued_Returns409InvalidStateAndNoNewLink()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();
        await journey.IssueLinkAsync(recoveryId, "link-op-1");

        HttpResponseMessage response = await journey.IssueLinkAsync(recoveryId, "link-op-2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", (await response.ReadJsonAsync()).ErrorCode());
        Assert.Single(await factory.Store.GetInboxAsync("alice", 50), m => m.Kind == InboxMessageKinds.ResetLink);
    }

    [Fact]
    public async Task IssueLink_UnverifiedRecovery_Returns409InvalidState()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.IssueLinkAsync(recoveryId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task IssueLink_DecoyRecovery_AnswersLikeUnverifiedKnownRecovery()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string knownId = await journey.StartAsync("alice");
        string decoyId = await journey.StartAsync("nobody.here");

        HttpResponseMessage known = await journey.IssueLinkAsync(knownId);
        HttpResponseMessage decoy = await journey.IssueLinkAsync(decoyId);

        Assert.Equal(known.StatusCode, decoy.StatusCode);
        Assert.Equal(await known.ReadTextAsync(), await decoy.ReadTextAsync());
        Assert.Empty(await factory.Store.GetInboxAsync("nobody.here", 50));
    }

    [Fact]
    public async Task IssueLink_AfterVerificationDeadline_Returns410RecoveryExpired()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();
        factory.Time.Advance(TimeSpan.FromSeconds(120));

        HttpResponseMessage response = await journey.IssueLinkAsync(recoveryId);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("recovery_expired", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task GetRecovery_AfterLinkIssued_ReportsLinkIssuedThenExpiredAfterTenMinutes()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, _) = await journey.ReadyForResetAsync();

        JsonElement issued = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();
        factory.Time.Advance(TimeSpan.FromMinutes(10));
        JsonElement expired = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();

        Assert.Equal("link_issued", issued.Text("status"));
        Assert.Equal(MocksFactory.StartTime.AddMinutes(10), issued.GetProperty("link_expires_at").GetDateTimeOffset());
        Assert.Equal("expired", expired.Text("status"));
    }

    [Fact]
    public async Task StartRecovery_WhileLinkIsValid_ThrottledUntilLinkExpires()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.ReadyForResetAsync();
        factory.Time.Advance(TimeSpan.FromMinutes(5));

        HttpResponseMessage response = await journey.StartRawAsync("alice");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task IssueLink_UnknownRecovery_Returns404NotFound()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.IssueLinkAsync("rec_doesnotexist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IssueLink_EmptyOperationId_Returns400InvalidRequest()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAndVerifyAsync();

        HttpResponseMessage response = await journey.IssueLinkAsync(recoveryId, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.ResetLinkTests"`
Expected: `failed: 10` or more (route missing).

- [ ] **Step 3: Write `ResetLinkService`**

`src/VoiceReset.Mocks/Issuer/ResetLinkService.cs`:

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Issuer;

/// <summary>
/// Issues the one reset link of a verified recovery and delivers it to the inbox.
/// The service caller never receives the link or the token.
/// </summary>
public sealed class ResetLinkService(
    IMockStore store,
    IOptions<MocksOptions> options,
    TimeProvider time,
    ILogger<ResetLinkService> logger)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(10);

    public async Task<ApiResult> IssueAsync(string recoveryId, ResetLinkRequest request)
    {
        if (!RequestRules.IsValidId(request.OperationId))
        {
            return ApiResult.InvalidRequest();
        }

        for (int attempt = 0; attempt < StoreRetry.MaxAttempts; attempt++)
        {
            RecoveryRecord? recovery = await store.GetAsync<RecoveryRecord>(recoveryId);
            if (recovery is null)
            {
                return ApiResult.NotFound();
            }

            if (recovery.LinkOperationId is not null)
            {
                return recovery.LinkOperationId == request.OperationId
                    ? LinkIssued(recovery)
                    : ApiResult.InvalidState("A reset link was already issued for this recovery.");
            }

            DateTimeOffset now = time.GetUtcNow();
            string status = recovery.StatusAt(now);
            if (status == RecoveryStatus.Expired)
            {
                return ApiResult.Error(StatusCodes.Status410Gone, "recovery_expired", "The recovery has expired.");
            }

            if (status != RecoveryStatus.Verified)
            {
                return ApiResult.InvalidState("The recovery is not verified.");
            }

            string token = Secrets.NewToken();
            string tokenHash = Secrets.Sha256Hex(token);
            DateTimeOffset linkExpiresAt = now + LinkLifetime;
            recovery.Status = RecoveryStatus.LinkIssued;
            recovery.LinkOperationId = request.OperationId;
            recovery.TokenHash = tokenHash;
            recovery.LinkExpiresAt = linkExpiresAt;
            if (!await store.TrySaveAsync(recovery))
            {
                continue; // someone else changed the recovery: read it again and decide again
            }

            await store.TrySaveAsync(new TokenRecord { TokenHash = tokenHash, RecoveryId = recovery.RecoveryId });
            string link = $"{options.Value.ResetBaseUrl}#token={Uri.EscapeDataString(token)}";
            await store.AddInboxMessageAsync(new InboxMessage(
                recovery.Username, Secrets.NewId("msg"), now, InboxMessageKinds.ResetLink, null, link, linkExpiresAt));
            MockLog.LinkIssued(logger, recovery.RecoveryId);
            return LinkIssued(recovery);
        }

        return ApiResult.DependencyUnavailable();
    }

    private static ApiResult LinkIssued(RecoveryRecord recovery) =>
        ApiResult.Ok(new { recovery.RecoveryId, Status = RecoveryStatus.LinkIssued, recovery.LinkExpiresAt });
}
```

- [ ] **Step 4: Add the route and register the service**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, add after the GET recovery route line:

```csharp
        service.MapPost("/recoveries/{id}/reset-link", IssueResetLinkAsync);
```

and add this handler after `GetRecoveryAsync`:

```csharp

    private static async Task<ApiResult> IssueResetLinkAsync(string id, HttpRequest http, ResetLinkService links) =>
        await MockJson.ReadAsync<ResetLinkRequest>(http) is { } request
            ? await links.IssueAsync(id, request)
            : ApiResult.InvalidRequest();
```

In `src/VoiceReset.Mocks/Program.cs`, add after `builder.Services.AddSingleton<VerificationService>();`:

```csharp
builder.Services.AddSingleton<ResetLinkService>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.ResetLinkTests"`
Expected: `Passed!`, `succeeded: 11`, `failed: 0`.

- [ ] **Step 6: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Issuer/ResetLinkTests.cs
git commit -m "feat(mocks): add reset link issuance and inbox delivery"
```

---

### Task 11: Password policy, `GET /v1/policy` and `POST /v1/password/validate`

**Files:**
- Create: `src/VoiceReset.Mocks/Issuer/PasswordPolicy.cs`
- Create: `src/VoiceReset.Mocks/Issuer/ResetService.cs` (complete; the reset and operation-status methods get their routes and tests in Tasks 12 to 14)
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/PasswordPolicyTests.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/PasswordValidateTests.cs`

**Rules implemented here.** The policy is versioned (`2026-10-v1`) and its descriptions are fixed, safe texts. `validate` needs a valid, unused, unexpired token (`401 invalid_token`, `409 token_used`, `410 link_expired`), never changes anything, never consumes the token, and never echoes the password. "Current password" is the configured `InitialPassword` until the first reset, then the hash saved by that reset.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/PasswordPolicyTests.cs`:

```csharp
using VoiceReset.Mocks.Issuer;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Short-1a", "min_length_12")]
    [InlineData("all-lowercase-123", "has_upper")]
    [InlineData("ALL-UPPERCASE-123", "has_lower")]
    [InlineData("No-Digits-Here-At-All", "has_digit")]
    [InlineData("My-Alice-Password-1", "not_contains_username")]
    public void FindViolations_WeakPassword_ReportsRule(string password, string expectedCode)
    {
        IReadOnlyList<PolicyRule> violations = PasswordPolicy.FindViolations(password, "alice", isCurrentPassword: false);

        Assert.Contains(violations, rule => rule.Code == expectedCode);
    }

    [Fact]
    public void FindViolations_TooLong_ReportsMaxLength()
    {
        string password = "Aa1" + new string('x', 200);

        IReadOnlyList<PolicyRule> violations = PasswordPolicy.FindViolations(password, "alice", isCurrentPassword: false);

        Assert.Contains(violations, rule => rule.Code == "max_length_128");
    }

    [Fact]
    public void FindViolations_CurrentPassword_ReportsNotCurrentPassword()
    {
        IReadOnlyList<PolicyRule> violations = PasswordPolicy.FindViolations("Good-Password-123", "alice", isCurrentPassword: true);

        Assert.Equal("not_current_password", Assert.Single(violations).Code);
    }

    [Fact]
    public void FindViolations_GoodPassword_ReturnsNoViolations()
    {
        IReadOnlyList<PolicyRule> violations = PasswordPolicy.FindViolations("Good-Password-123", "alice", isCurrentPassword: false);

        Assert.Empty(violations);
    }
}
```

`tests/VoiceReset.Mocks.Tests/Issuer/PasswordValidateTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class PasswordValidateTests
{
    [Fact]
    public async Task GetPolicy_NoAuthentication_Returns200WithVersionAndSafeRules()
    {
        using MocksFactory factory = new();
        HttpClient browser = factory.CreateClient();

        HttpResponseMessage response = await browser.GetAsync("/v1/policy", TestApi.Ct);
        JsonElement json = await response.ReadJsonAsync();
        string[] codes = json.GetProperty("rules").EnumerateArray().Select(rule => rule.Text("code")).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-10-v1", json.Text("policy_version"));
        Assert.Equal(
            new[] { "min_length_12", "max_length_128", "has_upper", "has_lower", "has_digit", "not_contains_username", "not_current_password" },
            codes);
        Assert.All(json.GetProperty("rules").EnumerateArray(), rule => Assert.Equal(new[] { "code", "description" }, rule.PropertyNames()));
    }

    [Fact]
    public async Task ValidatePassword_GoodPassword_Returns200ValidWithNoViolations()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage response = await journey.ValidateAsync(token, TestJourney.NewPassword);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "policy_version", "valid", "violations" }, json.PropertyNames());
        Assert.True(json.GetProperty("valid").GetBoolean());
        Assert.Empty(json.GetProperty("violations").EnumerateArray());
    }

    [Fact]
    public async Task ValidatePassword_WeakPassword_ReturnsSafeViolationsWithoutEcho()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage response = await journey.ValidateAsync(token, "weakpw");
        string body = await response.ReadTextAsync();
        JsonElement json = await response.ReadJsonAsync();
        string[] codes = json.GetProperty("violations").EnumerateArray().Select(v => v.Text("code")).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.GetProperty("valid").GetBoolean());
        Assert.Equal(new[] { "min_length_12", "has_upper", "has_digit" }, codes);
        Assert.DoesNotContain("weakpw", body);
    }

    [Fact]
    public async Task ValidatePassword_ContainsUsername_ReportsNotContainsUsername()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage response = await journey.ValidateAsync(token, "Strong-ALICE-Pass-9");
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal("not_contains_username", Assert.Single(json.GetProperty("violations").EnumerateArray()).Text("code"));
    }

    [Fact]
    public async Task ValidatePassword_InitialPassword_ReportsNotCurrentPassword()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync("bob");

        HttpResponseMessage response = await journey.ValidateAsync(token, MocksFactory.BobInitialPassword);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal("not_current_password", Assert.Single(json.GetProperty("violations").EnumerateArray()).Text("code"));
    }

    [Fact]
    public async Task ValidatePassword_UnknownToken_Returns401InvalidToken()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.ValidateAsync("not-a-real-token", TestJourney.NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_token", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task ValidatePassword_LinkOlderThanTenMinutes_Returns410LinkExpired()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        factory.Time.Advance(TimeSpan.FromMinutes(10));

        HttpResponseMessage response = await journey.ValidateAsync(token, TestJourney.NewPassword);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("link_expired", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task ValidatePassword_CalledTwice_DoesNotConsumeTheToken()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ValidateAsync(token, TestJourney.NewPassword);

        HttpResponseMessage second = await journey.ValidateAsync(token, TestJourney.NewPassword);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task ValidatePassword_UnexpectedField_Returns400InvalidRequest()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage response = await journey.Browser.PostJsonAsync(
            "/v1/password/validate", new { token, password = TestJourney.NewPassword, admin = "true" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.PasswordPolicyTests" --filter-class "VoiceReset.Mocks.Tests.Issuer.PasswordValidateTests"`
Expected: build error `CS0246: The type or namespace name 'PolicyRule' could not be found`.

- [ ] **Step 3: Write the policy**

`src/VoiceReset.Mocks/Issuer/PasswordPolicy.cs`:

```csharp
namespace VoiceReset.Mocks.Issuer;

/// <summary>One policy rule. The description is a fixed, safe text (fine to display or speak).</summary>
public sealed record PolicyRule(string Code, string Description);

/// <summary>The synthetic password policy. The reset backend enforces it; the browser only displays it.</summary>
public static class PasswordPolicy
{
    public const string Version = "2026-10-v1";
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static readonly IReadOnlyList<PolicyRule> Rules =
    [
        new("min_length_12", "Use at least 12 characters."),
        new("max_length_128", "Use at most 128 characters."),
        new("has_upper", "Include at least one uppercase letter (A-Z)."),
        new("has_lower", "Include at least one lowercase letter (a-z)."),
        new("has_digit", "Include at least one digit (0-9)."),
        new("not_contains_username", "Do not include your username."),
        new("not_current_password", "Do not reuse your current password."),
    ];

    /// <summary>The broken rules, in policy order. Never contains any part of the password.</summary>
    public static IReadOnlyList<PolicyRule> FindViolations(string password, string username, bool isCurrentPassword)
    {
        HashSet<string> failed = new(StringComparer.Ordinal);
        if (password.Length < MinLength)
        {
            failed.Add("min_length_12");
        }

        if (password.Length > MaxLength)
        {
            failed.Add("max_length_128");
        }

        if (!password.Any(char.IsAsciiLetterUpper))
        {
            failed.Add("has_upper");
        }

        if (!password.Any(char.IsAsciiLetterLower))
        {
            failed.Add("has_lower");
        }

        if (!password.Any(char.IsAsciiDigit))
        {
            failed.Add("has_digit");
        }

        if (password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            failed.Add("not_contains_username");
        }

        if (isCurrentPassword)
        {
            failed.Add("not_current_password");
        }

        return Rules.Where(rule => failed.Contains(rule.Code)).ToList();
    }
}
```

- [ ] **Step 4: Write `ResetService`**

`src/VoiceReset.Mocks/Issuer/ResetService.cs`:

```csharp
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Issuer;

/// <summary>
/// The browser-facing part of the issuer: password validation, the reset itself,
/// and reset operation status. The token in the body authorizes only its own
/// recovery; a reset re-checks binding, expiry, single use, verification and policy.
/// </summary>
public sealed class ResetService(
    IMockStore store,
    SyntheticUsers users,
    ResetCompletion completion,
    IOptions<FaultsOptions> faults,
    TimeProvider time,
    ILogger<ResetService> logger)
{
    private const string ResetAction = "reset";
    private const int MaxTokenLength = 256;
    private const int MaxPasswordLength = 1024;

    public async Task<ApiResult> ValidateAsync(ValidatePasswordRequest request)
    {
        if (!IsValidToken(request.Token) || request.Password.Length > MaxPasswordLength)
        {
            return ApiResult.InvalidRequest();
        }

        RecoveryRecord? recovery = await FindByTokenAsync(request.Token);
        if (recovery is null)
        {
            return ApiResult.InvalidToken();
        }

        if (TokenNotUsable(recovery) is { } tokenError)
        {
            return tokenError;
        }

        IReadOnlyList<PolicyRule> violations = await FindViolationsAsync(recovery, request.Password);
        return ApiResult.Ok(new { Valid = violations.Count == 0, PolicyVersion = PasswordPolicy.Version, Violations = violations });
    }

    public async Task<ApiResult> ResetAsync(ResetRequest request)
    {
        if (!IsValidToken(request.Token) || request.NewPassword.Length > MaxPasswordLength || !RequestRules.IsValidId(request.OperationId))
        {
            return ApiResult.InvalidRequest();
        }

        // Recognises an identical retry without keeping the password (the token is never stored).
        string fingerprint = Secrets.Sha256Hex($"{request.Token}\n{request.NewPassword}");

        for (int attempt = 0; attempt < StoreRetry.MaxAttempts; attempt++)
        {
            RecoveryRecord? recovery = await FindByTokenAsync(request.Token);
            if (recovery is null)
            {
                return ApiResult.InvalidToken();
            }

            // An identical retry of the accepted operation returns its result, even after the
            // token was used or expired. It never resets twice and never authorizes a new operation.
            if (recovery.ResetOperationId == request.OperationId)
            {
                return Secrets.FixedTimeEquals(recovery.ResetFingerprint ?? "", fingerprint)
                    ? OperationResult(recovery, forStatusRead: false)
                    : ApiResult.IdempotencyConflict();
            }

            if (TokenNotUsable(recovery) is { } tokenError)
            {
                return tokenError;
            }

            IdempotencyRecord? operation = await store.GetAsync<IdempotencyRecord>(IdempotencyRecord.KeyFor(ResetAction, request.OperationId));
            if (operation is not null && operation.RecoveryId != recovery.RecoveryId)
            {
                return ApiResult.IdempotencyConflict(); // this operation ID belongs to another recovery
            }

            IReadOnlyList<PolicyRule> violations = await FindViolationsAsync(recovery, request.NewPassword);
            if (violations.Count > 0)
            {
                return PolicyViolation(violations); // nothing is saved: the token stays usable
            }

            if (operation is null && !await store.TrySaveAsync(new IdempotencyRecord
            {
                Action = ResetAction,
                IdempotencyKey = request.OperationId,
                Fingerprint = fingerprint,
                RecoveryId = recovery.RecoveryId,
            }))
            {
                continue; // another request registered this operation ID just now: read again
            }

            // Reserve the token for this operation. The ETag makes this atomic: a concurrent
            // request with another operation ID fails here, reads again and gets token_used.
            recovery.Status = RecoveryStatus.ResetPending;
            recovery.ResetOperationId = request.OperationId;
            recovery.ResetFingerprint = fingerprint;
            recovery.NewPasswordHash = Secrets.HashPassword(request.NewPassword);
            recovery.ResetCompletesAt = time.GetUtcNow().AddSeconds(faults.Value.ResetCompletionDelaySeconds);
            if (!await store.TrySaveAsync(recovery))
            {
                continue;
            }

            MockLog.ResetAccepted(logger, request.OperationId, recovery.RecoveryId);

            // With no delay configured the reset is due now, so this completes it at once.
            RecoveryRecord current = await completion.LoadAsync(recovery.RecoveryId) ?? recovery;
            return OperationResult(current, forStatusRead: false);
        }

        return ApiResult.DependencyUnavailable();
    }

    /// <summary>
    /// GET /v1/reset-operations/{id}. With <paramref name="resetToken"/> (browser) only the
    /// operation bound to that token is readable; anything else is 404, like a missing operation.
    /// </summary>
    public async Task<ApiResult> GetOperationAsync(string operationId, string? resetToken)
    {
        IdempotencyRecord? operation = await store.GetAsync<IdempotencyRecord>(IdempotencyRecord.KeyFor(ResetAction, operationId));
        if (operation?.RecoveryId is null)
        {
            return ApiResult.NotFound();
        }

        RecoveryRecord? recovery = await completion.LoadAsync(operation.RecoveryId);
        if (recovery is null || recovery.ResetOperationId != operationId)
        {
            return ApiResult.NotFound(); // for example an operation that lost the race for the token
        }

        if (resetToken is not null && !Secrets.FixedTimeEquals(recovery.TokenHash ?? "", Secrets.Sha256Hex(resetToken)))
        {
            return ApiResult.NotFound();
        }

        return OperationResult(recovery, forStatusRead: true);
    }

    private static bool IsValidToken(string token) => token.Length is > 0 and <= MaxTokenLength;

    private async Task<RecoveryRecord?> FindByTokenAsync(string token)
    {
        string tokenHash = Secrets.Sha256Hex(token);
        TokenRecord? entry = await store.GetAsync<TokenRecord>(tokenHash);
        if (entry is null)
        {
            return null;
        }

        RecoveryRecord? recovery = await completion.LoadAsync(entry.RecoveryId);
        return recovery?.TokenHash is { } storedHash && Secrets.FixedTimeEquals(storedHash, tokenHash) ? recovery : null;
    }

    private ApiResult? TokenNotUsable(RecoveryRecord recovery)
    {
        if (recovery.ResetOperationId is not null)
        {
            return ApiResult.Error(StatusCodes.Status409Conflict, "token_used", "This reset link has already been used.");
        }

        if (time.GetUtcNow() >= recovery.LinkExpiresAt)
        {
            return ApiResult.Error(StatusCodes.Status410Gone, "link_expired", "This reset link has expired.");
        }

        return null;
    }

    private async Task<IReadOnlyList<PolicyRule>> FindViolationsAsync(RecoveryRecord recovery, string password)
    {
        AccountRecord? account = await store.GetAsync<AccountRecord>(recovery.Username);
        string? initialPassword = users.Find(recovery.Username)?.InitialPassword;
        bool isCurrentPassword = account?.PasswordHash is { } passwordHash
            ? Secrets.VerifyPassword(password, passwordHash)
            : initialPassword is not null && Secrets.FixedTimeEquals(Secrets.Sha256Hex(password), Secrets.Sha256Hex(initialPassword));
        return PasswordPolicy.FindViolations(password, recovery.Username, isCurrentPassword);
    }

    private static ApiResult PolicyViolation(IReadOnlyList<PolicyRule> violations) =>
        ApiResult.Of(StatusCodes.Status422UnprocessableEntity, new
        {
            Error = ApiResult.ErrorBody("policy_violation", "The new password does not meet the password policy."),
            PolicyVersion = PasswordPolicy.Version,
            Violations = violations,
        });

    /// <summary>
    /// The contract's operation result. Pending: receipt, unlock status and reason are null.
    /// Succeeded: issuer receipt + unlock status. (This mock has no failure path; see the docs.)
    /// </summary>
    private static ApiResult OperationResult(RecoveryRecord recovery, bool forStatusRead)
    {
        string status = recovery.Status == RecoveryStatus.Completed ? "succeeded" : "pending";
        string? reasonCode = null;
        object body = forStatusRead
            ? new { OperationId = recovery.ResetOperationId, recovery.RecoveryId, Status = status, recovery.ResetReceipt, recovery.UnlockStatus, ReasonCode = reasonCode }
            : new { OperationId = recovery.ResetOperationId, Status = status, recovery.ResetReceipt, recovery.UnlockStatus, ReasonCode = reasonCode };
        int statusCode = !forStatusRead && status == "pending" ? StatusCodes.Status202Accepted : StatusCodes.Status200OK;
        return ApiResult.Of(statusCode, body);
    }
}
```

- [ ] **Step 5: Add the browser routes and register the service**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, add at the end of `MapIssuerEndpoints` (after the reset-link route):

```csharp

        // Browser routes: no service credential. The token in the body is the authorization.
        RouteGroupBuilder browser = app.MapGroup("/v1");

        browser.MapGet("/policy", GetPolicy);
        browser.MapPost("/password/validate", ValidatePasswordAsync);
```

and add these handlers after `IssueResetLinkAsync`:

```csharp

    private static ApiResult GetPolicy() =>
        ApiResult.Ok(new { PolicyVersion = PasswordPolicy.Version, PasswordPolicy.Rules });

    private static async Task<ApiResult> ValidatePasswordAsync(HttpRequest http, ResetService resets) =>
        await MockJson.ReadAsync<ValidatePasswordRequest>(http) is { } request
            ? await resets.ValidateAsync(request)
            : ApiResult.InvalidRequest();
```

In `src/VoiceReset.Mocks/Program.cs`, add after `builder.Services.AddSingleton<ResetLinkService>();`:

```csharp
builder.Services.AddSingleton<ResetService>();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.PasswordPolicyTests" --filter-class "VoiceReset.Mocks.Tests.Issuer.PasswordValidateTests"`
Expected: `Passed!`, `succeeded: 17`, `failed: 0`.

- [ ] **Step 7: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Issuer
git commit -m "feat(mocks): add password policy and validation"
```

---

### Task 12: `POST /v1/resets`

**Files:**
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/ResetTests.cs`
- (Code under test: `ResetService.ResetAsync` and `ResetCompletion` from Tasks 6 and 11)

**Rules checked here.** Policy rejection → `422 policy_violation` with safe violations, no reset, token still usable. Accepted → the token is reserved for that `operation_id` with one ETag write (single use). A different operation → `409 token_used`, also when two arrive at the same moment. An identical retry returns the recorded result even after the token expired. Same operation with another password → `409 idempotency_conflict`. Success has an opaque receipt (`rcpt_...`) bound to the operation/account/recovery, and `unlock_status` from the user's `RequiresUnlock`.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/ResetTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class ResetTests
{
    [Fact]
    public async Task Reset_ValidTokenAndPassword_Returns200SucceededWithReceiptAndUnlock()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync("alice");

        HttpResponseMessage response = await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "operation_id", "reason_code", "reset_receipt", "status", "unlock_status" }, json.PropertyNames());
        Assert.Equal("op-1", json.Text("operation_id"));
        Assert.Equal("succeeded", json.Text("status"));
        Assert.StartsWith("rcpt_", json.Text("reset_receipt"));
        Assert.Equal("unlocked", json.Text("unlock_status"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reason_code").ValueKind);
    }

    [Fact]
    public async Task Reset_UserWithoutUnlockRequirement_ReportsNotRequired()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync("bob");

        HttpResponseMessage response = await journey.ResetAsync(token, TestJourney.NewPassword);

        Assert.Equal("not_required", (await response.ReadJsonAsync()).Text("unlock_status"));
    }

    [Fact]
    public async Task GetRecovery_AfterReset_ReportsCompletedWithTheSameReceipt()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string receipt) = await journey.CompleteResetAsync();

        JsonElement json = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();

        Assert.Equal("completed", json.Text("status"));
        Assert.Equal(receipt, json.Text("reset_receipt"));
        Assert.Equal("unlocked", json.Text("unlock_status"));
        Assert.Equal(JsonValueKind.String, json.GetProperty("reset_operation_id").ValueKind);
    }

    [Fact]
    public async Task Reset_PolicyViolation_Returns422WithoutEchoAndKeepsTokenUsable()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage rejected = await journey.ResetAsync(token, "weakpw");
        string body = await rejected.ReadTextAsync();
        JsonElement json = await rejected.ReadJsonAsync();
        JsonElement status = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();
        HttpResponseMessage corrected = await journey.ResetAsync(token, TestJourney.NewPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal("policy_violation", json.ErrorCode());
        Assert.Equal("2026-10-v1", json.Text("policy_version"));
        Assert.NotEmpty(json.GetProperty("violations").EnumerateArray());
        Assert.DoesNotContain("weakpw", body);
        Assert.Equal("link_issued", status.Text("status"));
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
    }

    [Fact]
    public async Task Reset_SecondOperationAfterSuccess_Returns409TokenUsed()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        HttpResponseMessage response = await journey.ResetAsync(token, "Another-Synthetic-Pass-7", "op-2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("token_used", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Reset_IdenticalRetryAfterTokenExpired_ReturnsTheRecordedResult()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        HttpResponseMessage first = await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");
        factory.Time.Advance(TimeSpan.FromMinutes(11));

        HttpResponseMessage retry = await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
    }

    [Fact]
    public async Task Reset_SameOperationDifferentPassword_Returns409IdempotencyConflict()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        HttpResponseMessage response = await journey.ResetAsync(token, "Another-Synthetic-Pass-7", "op-1");

        Assert.Equal("idempotency_conflict", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Reset_TwoOperationsAtTheSameTime_OneSucceedsAndTheOtherGetsTokenUsed()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            journey.ResetAsync(token, TestJourney.NewPassword, "op-a"),
            journey.ResetAsync(token, "Another-Synthetic-Pass-7", "op-b"));
        HttpResponseMessage winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        JsonElement status = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();

        Assert.Equal("token_used", (await loser.ReadJsonAsync()).ErrorCode());
        Assert.Equal((await winner.ReadJsonAsync()).Text("operation_id"), status.Text("reset_operation_id"));
    }

    [Fact]
    public async Task Reset_LinkOlderThanTenMinutes_Returns410LinkExpired()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        factory.Time.Advance(TimeSpan.FromMinutes(10));

        HttpResponseMessage response = await journey.ResetAsync(token, TestJourney.NewPassword);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("link_expired", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Reset_UnknownToken_Returns401InvalidToken()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.ResetAsync("not-a-real-token", TestJourney.NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_token", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task Reset_OperationIdAlreadyUsedByAnotherRecovery_Returns409IdempotencyConflict()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string aliceToken) = await journey.ReadyForResetAsync("alice");
        (_, string bobToken) = await journey.ReadyForResetAsync("bob");
        await journey.ResetAsync(aliceToken, TestJourney.NewPassword, "shared-op");

        HttpResponseMessage response = await journey.ResetAsync(bobToken, TestJourney.NewPassword, "shared-op");

        Assert.Equal("idempotency_conflict", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task ValidatePassword_AfterReset_Returns409TokenUsed()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword);

        HttpResponseMessage response = await journey.ValidateAsync(token, "Another-Synthetic-Pass-7");

        Assert.Equal("token_used", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task ValidatePassword_NextRecoveryAfterReset_NewPasswordIsNowCurrent()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        await journey.CompleteResetAsync();
        factory.Time.Advance(TimeSpan.FromSeconds(120));
        (_, string nextToken) = await journey.ReadyForResetAsync();

        HttpResponseMessage response = await journey.ValidateAsync(nextToken, TestJourney.NewPassword);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal("not_current_password", Assert.Single(json.GetProperty("violations").EnumerateArray()).Text("code"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.ResetTests"`
Expected: `failed: 13` (route missing).

- [ ] **Step 3: Add the route**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, add after `browser.MapPost("/password/validate", ValidatePasswordAsync);`:

```csharp
        browser.MapPost("/resets", ResetAsync);
```

and add this handler after `ValidatePasswordAsync`:

```csharp

    private static async Task<ApiResult> ResetAsync(HttpRequest http, ResetService resets) =>
        await MockJson.ReadAsync<ResetRequest>(http) is { } request
            ? await resets.ResetAsync(request)
            : ApiResult.InvalidRequest();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.ResetTests"`
Expected: `Passed!`, `succeeded: 13`, `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs tests/VoiceReset.Mocks.Tests/Issuer/ResetTests.cs
git commit -m "feat(mocks): add password reset with single-use token and receipts"
```

---

### Task 13: Delayed reset completion (`Faults:ResetCompletionDelaySeconds`)

**Files:**
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/DelayedResetTests.cs`
- (Code under test: `ResetService` and `ResetCompletion`; nothing new to write)

With a delay above 0 the reset answers `202 pending`, and the recovery shows `reset_pending` with no receipt. The first read after the delay (any route that loads the recovery) commits the new password and creates the receipt. There is no background job: completion is checked lazily with `TimeProvider`. These tests pass at once; Step 3 proves they would catch a regression.

- [ ] **Step 1: Write the tests**

`tests/VoiceReset.Mocks.Tests/Issuer/DelayedResetTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class DelayedResetTests
{
    private const int DelaySeconds = 30;

    [Fact]
    public async Task Reset_WithCompletionDelay_Returns202PendingWithNullResultFields()
    {
        using MocksFactory factory = new(resetDelaySeconds: DelaySeconds);
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();

        HttpResponseMessage response = await journey.ResetAsync(token, TestJourney.NewPassword);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending", json.Text("status"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reset_receipt").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("unlock_status").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reason_code").ValueKind);
    }

    [Fact]
    public async Task GetRecovery_WhileResetPending_ReportsResetPendingWithoutReceipt()
    {
        using MocksFactory factory = new(resetDelaySeconds: DelaySeconds);
        TestJourney journey = new(factory);
        (string recoveryId, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword);
        factory.Time.Advance(TimeSpan.FromSeconds(DelaySeconds - 1));

        JsonElement json = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();

        Assert.Equal("reset_pending", json.Text("status"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reset_receipt").ValueKind);
        Assert.Equal(JsonValueKind.String, json.GetProperty("reset_operation_id").ValueKind);
    }

    [Fact]
    public async Task Reset_RetriedWhilePending_StillAnswers202Pending()
    {
        using MocksFactory factory = new(resetDelaySeconds: DelaySeconds);
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        HttpResponseMessage retry = await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    }

    [Fact]
    public async Task GetRecovery_AfterDelay_ReportsCompletedWithReceipt()
    {
        using MocksFactory factory = new(resetDelaySeconds: DelaySeconds);
        TestJourney journey = new(factory);
        (string recoveryId, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword);
        factory.Time.Advance(TimeSpan.FromSeconds(DelaySeconds));

        JsonElement json = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();

        Assert.Equal("completed", json.Text("status"));
        Assert.StartsWith("rcpt_", json.Text("reset_receipt"));
        Assert.Equal("unlocked", json.Text("unlock_status"));
    }

    [Fact]
    public async Task Reset_RetriedAfterDelay_Returns200WithTheSameReceiptAsTheStatus()
    {
        using MocksFactory factory = new(resetDelaySeconds: DelaySeconds);
        TestJourney journey = new(factory);
        (string recoveryId, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");
        factory.Time.Advance(TimeSpan.FromSeconds(DelaySeconds));

        HttpResponseMessage retry = await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");
        JsonElement retryJson = await retry.ReadJsonAsync();
        JsonElement status = await (await journey.Service.GetAsync($"/v1/recoveries/{recoveryId}", TestApi.Ct)).ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("succeeded", retryJson.Text("status"));
        Assert.Equal(status.Text("reset_receipt"), retryJson.Text("reset_receipt"));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.DelayedResetTests"`
Expected: `Passed!`, `succeeded: 5`, `failed: 0`.

- [ ] **Step 3: Prove the tests guard the behaviour**

In `ResetService.ResetAsync`, temporarily change `.AddSeconds(faults.Value.ResetCompletionDelaySeconds)` to `.AddSeconds(0)` and run the same command.
Expected: `failed: 3` (the three tests that expect `pending`). Undo the change and run again: `failed: 0`.

- [ ] **Step 4: Commit**

```powershell
git add tests/VoiceReset.Mocks.Tests/Issuer/DelayedResetTests.cs
git commit -m "test(mocks): cover delayed reset completion"
```

---

### Task 14: `GET /v1/reset-operations/{operation_id}`

**Files:**
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Issuer/ResetOperationStatusTests.cs`
- (Code under test: `ResetService.GetOperationAsync` from Task 11)

Two ways in: the service credential (`Authorization: Bearer ...`) can read any operation; the browser (`Authorization: ResetToken <token>`) can read only the operation bound to its token, also after the token expired or was used. Anything else is `404 not_found`; no or wrong credential is `401 unauthenticated`. The token is sent in a header, never in the URL.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Issuer/ResetOperationStatusTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests.Issuer;

public sealed class ResetOperationStatusTests
{
    [Fact]
    public async Task GetOperation_ServiceCredential_Returns200WithRecoveryId()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        HttpResponseMessage response = await journey.Service.GetAsync("/v1/reset-operations/op-1", TestApi.Ct);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "operation_id", "reason_code", "recovery_id", "reset_receipt", "status", "unlock_status" }, json.PropertyNames());
        Assert.Equal(recoveryId, json.Text("recovery_id"));
        Assert.Equal("succeeded", json.Text("status"));
    }

    [Fact]
    public async Task GetOperation_MatchingResetToken_Returns200()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");

        HttpResponseMessage response = await GetWithResetTokenAsync(journey.Browser, "op-1", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOperation_MatchingResetTokenAfterTokenExpired_StillReadsItsOperation()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, TestJourney.NewPassword, "op-1");
        factory.Time.Advance(TimeSpan.FromMinutes(30));

        HttpResponseMessage response = await GetWithResetTokenAsync(journey.Browser, "op-1", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOperation_ResetTokenOfAnotherRecovery_Returns404()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string aliceToken) = await journey.ReadyForResetAsync("alice");
        (_, string bobToken) = await journey.ReadyForResetAsync("bob");
        await journey.ResetAsync(aliceToken, TestJourney.NewPassword, "op-1");

        HttpResponseMessage response = await GetWithResetTokenAsync(journey.Browser, "op-1", bobToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task GetOperation_UnknownOperation_Returns404()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.Service.GetAsync("/v1/reset-operations/op-unknown", TestApi.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOperation_PolicyRejectedOperation_Returns404()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string token) = await journey.ReadyForResetAsync();
        await journey.ResetAsync(token, "weakpw", "op-rejected");

        HttpResponseMessage response = await journey.Service.GetAsync("/v1/reset-operations/op-rejected", TestApi.Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOperation_NoAuthorization_Returns401Unauthenticated()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.Browser.GetAsync("/v1/reset-operations/op-1", TestApi.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task GetOperation_WrongBearerCredential_Returns401Unauthenticated()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-service-credential");

        HttpResponseMessage response = await client.GetAsync("/v1/reset-operations/op-1", TestApi.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetWithResetTokenAsync(HttpClient client, string operationId, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, $"/v1/reset-operations/{operationId}");
        request.Headers.TryAddWithoutValidation("Authorization", $"ResetToken {token}");
        return await client.SendAsync(request, TestApi.Ct);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.ResetOperationStatusTests"`
Expected: `failed: 8` (route missing; 404 without the JSON envelope).

- [ ] **Step 3: Add the route**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, add after `browser.MapPost("/resets", ResetAsync);`:

```csharp
        browser.MapGet("/reset-operations/{operationId}", GetResetOperationAsync);
```

and add this handler after `ResetAsync`:

```csharp

    /// <summary>Service credential, or "Authorization: ResetToken &lt;token&gt;" for the browser.</summary>
    private static async Task<ApiResult> GetResetOperationAsync(string operationId, HttpRequest http, ServiceAuth auth, ResetService resets)
    {
        const string resetTokenPrefix = "ResetToken ";
        string authorization = http.Headers.Authorization.ToString();
        if (authorization.StartsWith(resetTokenPrefix, StringComparison.Ordinal))
        {
            return await resets.GetOperationAsync(operationId, authorization[resetTokenPrefix.Length..]);
        }

        return auth.IsServiceRequest(http)
            ? await resets.GetOperationAsync(operationId, resetToken: null)
            : ApiResult.Unauthenticated();
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Issuer.ResetOperationStatusTests"`
Expected: `Passed!`, `succeeded: 8`, `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs tests/VoiceReset.Mocks.Tests/Issuer/ResetOperationStatusTests.cs
git commit -m "feat(mocks): add reset operation status endpoint"
```

---

### Task 15: `POST /v1/tickets`

**Files:**
- Create: `src/VoiceReset.Mocks/Tickets/TicketRequests.cs`
- Create: `src/VoiceReset.Mocks/Tickets/TicketService.cs` (complete; the outcome route and its tests come in Task 16)
- Create: `src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Tickets/CreateTicketTests.cs`

**Rules.** At most one ticket per recovery: the ticket ID is derived from the recovery ID (`tkt_` + recovery ID), so the insert itself guarantees uniqueness. The first creation answers `201 open`; any later creation with another `operation_id` answers `200` with the existing ticket and its **current** outcome (no reopening). The same `operation_id` replays its recorded answer; the same `operation_id` for another recovery is `409 idempotency_conflict`. A ticket can exist before verification, and for decoy recoveries too (so the response does not reveal whether the account exists).

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Tickets/CreateTicketTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace VoiceReset.Mocks.Tests.Tickets;

public sealed class CreateTicketTests
{
    [Fact]
    public async Task CreateTicket_NewRecovery_Returns201Open()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-op-1" });
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new[] { "outcome", "recovery_id", "ticket_id" }, json.PropertyNames());
        Assert.Equal(recoveryId, json.Text("recovery_id"));
        Assert.Equal("open", json.Text("outcome"));
        Assert.StartsWith("tkt_", json.Text("ticket_id"));
    }

    [Fact]
    public async Task CreateTicket_DecoyRecovery_Returns201LikeKnownRecovery()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string decoyId = await journey.StartAsync("nobody.here");

        HttpResponseMessage response = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = decoyId, operation_id = "t-op-1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateTicket_SecondRequestWithNewOperation_Returns200WithTheSameTicket()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        HttpResponseMessage first = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-op-1" });

        HttpResponseMessage second = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-op-2" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await first.ReadJsonAsync()).Text("ticket_id"), (await second.ReadJsonAsync()).Text("ticket_id"));
    }

    [Fact]
    public async Task CreateTicket_SameOperationRetried_ReplaysRecorded201()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        HttpResponseMessage first = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-op-1" });

        HttpResponseMessage retry = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-op-1" });

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
    }

    [Fact]
    public async Task CreateTicket_SameOperationForAnotherRecovery_Returns409IdempotencyConflict()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string aliceRecovery = await journey.StartAsync("alice");
        string bobRecovery = await journey.StartAsync("bob");
        await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = aliceRecovery, operation_id = "t-op-1" });

        HttpResponseMessage response = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = bobRecovery, operation_id = "t-op-1" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_conflict", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task CreateTicket_UnknownRecovery_Returns404NotFound()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = "rec_doesnotexist", operation_id = "t-op-1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTicket_NoAuthorization_Returns401Unauthenticated()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostJsonAsync("/v1/tickets", new { recovery_id = "rec_x", operation_id = "t-op-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTicket_MissingOperationId_Returns400InvalidRequest()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();

        HttpResponseMessage response = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Tickets.CreateTicketTests"`
Expected: `failed: 8` (route missing).

- [ ] **Step 3: Write the request records**

`src/VoiceReset.Mocks/Tickets/TicketRequests.cs`:

```csharp
namespace VoiceReset.Mocks.Tickets;

// Request bodies of the ticket routes (strict JSON: every field must be present).

public sealed record CreateTicketRequest(string RecoveryId, string OperationId);

/// <summary><c>reset_receipt</c> is the only nullable request field in the contract; it must still be sent.</summary>
public sealed record TicketOutcomeRequest(string Outcome, string? ResetReceipt, string ReasonCode, string OperationId);
```

- [ ] **Step 4: Write `TicketService`**

`src/VoiceReset.Mocks/Tickets/TicketService.cs`:

```csharp
using VoiceReset.Mocks.Issuer;
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tickets;

/// <summary>
/// The mock help-desk ticket system: one ticket per recovery and a truthful outcome.
/// "resolved" needs a receipt the issuer itself created for this ticket's recovery.
/// </summary>
public sealed class TicketService(IMockStore store, ResetCompletion completion, TimeProvider time, ILogger<TicketService> logger)
{
    private const string CreateAction = "ticket-create";

    /// <summary>One ticket per recovery: the ID is derived from the recovery ID, so a second insert fails.</summary>
    public static string TicketIdFor(string recoveryId) => $"tkt_{recoveryId}";

    public async Task<ApiResult> CreateAsync(CreateTicketRequest request)
    {
        if (!RequestRules.IsValidId(request.RecoveryId) || !RequestRules.IsValidId(request.OperationId))
        {
            return ApiResult.InvalidRequest();
        }

        string fingerprint = Secrets.Sha256Hex(request.RecoveryId);
        IdempotencyRecord? previous = await store.GetAsync<IdempotencyRecord>(IdempotencyRecord.KeyFor(CreateAction, request.OperationId));
        if (previous is not null)
        {
            return previous.Fingerprint == fingerprint
                ? new ApiResult(previous.StatusCode, previous.Json)
                : ApiResult.IdempotencyConflict();
        }

        RecoveryRecord? recovery = await store.GetAsync<RecoveryRecord>(request.RecoveryId);
        if (recovery is null)
        {
            return ApiResult.NotFound();
        }

        TicketRecord ticket = new() { TicketId = TicketIdFor(recovery.RecoveryId), RecoveryId = recovery.RecoveryId };
        ApiResult result;
        if (await store.TrySaveAsync(ticket))
        {
            result = ApiResult.Of(StatusCodes.Status201Created, Summary(ticket));
        }
        else
        {
            // The ticket exists already: report it with its current outcome, never reopen it.
            TicketRecord existing = await store.GetAsync<TicketRecord>(ticket.TicketId) ?? ticket;
            result = ApiResult.Ok(Summary(existing));
        }

        await store.TrySaveAsync(new IdempotencyRecord
        {
            Action = CreateAction,
            IdempotencyKey = request.OperationId,
            Fingerprint = fingerprint,
            RecoveryId = recovery.RecoveryId,
            StatusCode = result.StatusCode,
            Json = result.Json,
        });
        return result;
    }

    public async Task<ApiResult> RecordOutcomeAsync(string ticketId, TicketOutcomeRequest request)
    {
        if (!IsWellFormed(request))
        {
            return ApiResult.InvalidRequest();
        }

        string fingerprint = Secrets.Sha256Hex($"{request.Outcome}\n{request.ResetReceipt}\n{request.ReasonCode}");
        for (int attempt = 0; attempt < StoreRetry.MaxAttempts; attempt++)
        {
            TicketRecord? ticket = await store.GetAsync<TicketRecord>(ticketId);
            if (ticket is null)
            {
                return ApiResult.NotFound();
            }

            // The history doubles as the idempotency record for outcome updates.
            TicketHistoryEntry? previous = ticket.History.Find(entry => entry.OperationId == request.OperationId);
            if (previous is not null)
            {
                return previous.Fingerprint == fingerprint
                    ? new ApiResult(previous.StatusCode, previous.Json)
                    : ApiResult.IdempotencyConflict();
            }

            if (request.Outcome == TicketOutcomes.Resolved && !await IsIssuerReceiptAsync(ticket.RecoveryId, request.ResetReceipt ?? ""))
            {
                return ApiResult.InvalidState("The reset receipt is not valid for this ticket.");
            }

            bool changed = Apply(ticket, request);
            ApiResult result = ApiResult.Ok(new { ticket.TicketId, ticket.RecoveryId, ticket.Outcome, ticket.ResetReceipt, ticket.ReasonCode });
            ticket.History.Add(new TicketHistoryEntry(
                request.OperationId, fingerprint, request.Outcome, request.ReasonCode, changed, time.GetUtcNow(), result.StatusCode, result.Json));
            if (await store.TrySaveAsync(ticket))
            {
                MockLog.TicketOutcomeRecorded(logger, ticket.TicketId, request.Outcome, changed);
                return result;
            }
        }

        return ApiResult.DependencyUnavailable();
    }

    private static object Summary(TicketRecord ticket) => new { ticket.TicketId, ticket.RecoveryId, ticket.Outcome };

    /// <summary>"resolved" needs a receipt and reset_completed; every other outcome needs a null receipt and a safe reason.</summary>
    private static bool IsWellFormed(TicketOutcomeRequest request) =>
        RequestRules.IsValidId(request.OperationId) && request.Outcome switch
        {
            TicketOutcomes.Resolved =>
                request.ReasonCode == TicketOutcomes.ResetCompleted && !string.IsNullOrEmpty(request.ResetReceipt),
            TicketOutcomes.Escalated or TicketOutcomes.Cancelled or TicketOutcomes.Pending =>
                request.ResetReceipt is null && TicketOutcomes.SafeReasons.Contains(request.ReasonCode),
            _ => false,
        };

    /// <summary>
    /// Applies the update rules. Returns false when the earlier outcome is kept:
    /// a confirmed success is never overwritten, and a human request is never
    /// replaced by an automated result (a completed reset only adds its receipt).
    /// </summary>
    private static bool Apply(TicketRecord ticket, TicketOutcomeRequest update)
    {
        if (ticket.Outcome == TicketOutcomes.Resolved)
        {
            return false;
        }

        if (ticket.Outcome == TicketOutcomes.Escalated && ticket.ReasonCode == TicketOutcomes.HumanRequested)
        {
            if (update.Outcome == TicketOutcomes.Resolved)
            {
                ticket.ResetReceipt = update.ResetReceipt;
            }

            return false;
        }

        ticket.Outcome = update.Outcome;
        ticket.ReasonCode = update.ReasonCode;
        ticket.ResetReceipt = update.ResetReceipt;
        return true;
    }

    /// <summary>Checked against issuer state: the recovery must be completed with exactly this receipt.</summary>
    private async Task<bool> IsIssuerReceiptAsync(string recoveryId, string receipt)
    {
        RecoveryRecord? recovery = await completion.LoadAsync(recoveryId);
        return recovery is { Status: RecoveryStatus.Completed, ResetReceipt: { } issued }
            && Secrets.FixedTimeEquals(Secrets.Sha256Hex(issued), Secrets.Sha256Hex(receipt));
    }
}
```

- [ ] **Step 5: Write the endpoints and register them**

`src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs`:

```csharp
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Tickets;

/// <summary>The /v1/tickets routes (service credential only).</summary>
public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder tickets = app.MapGroup("/v1/tickets")
            .AddEndpointFilter<ServiceAuthFilter>();

        tickets.MapPost("", CreateTicketAsync);
    }

    private static async Task<ApiResult> CreateTicketAsync(HttpRequest http, TicketService tickets) =>
        await MockJson.ReadAsync<CreateTicketRequest>(http) is { } request
            ? await tickets.CreateAsync(request)
            : ApiResult.InvalidRequest();
}
```

In `src/VoiceReset.Mocks/Program.cs`, add `using VoiceReset.Mocks.Tickets;` to the `using` lines, add after `builder.Services.AddSingleton<ResetService>();`:

```csharp
builder.Services.AddSingleton<TicketService>();
```

and add after `app.MapIssuerEndpoints();`:

```csharp
app.MapTicketEndpoints();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Tickets.CreateTicketTests"`
Expected: `Passed!`, `succeeded: 8`, `failed: 0`.

- [ ] **Step 7: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Tickets/CreateTicketTests.cs
git commit -m "feat(mocks): add ticket creation"
```

---

### Task 16: `POST /v1/tickets/{id}/outcome`

**Files:**
- Modify: `src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Tickets/TicketOutcomeTests.cs`
- (Code under test: `TicketService.RecordOutcomeAsync` from Task 15)

**Rules.** `resolved` needs `reason_code: "reset_completed"` and a receipt that the issuer created for **this ticket's recovery** (checked against the recovery record, constant-time); a fabricated or cross-recovery receipt is `409 invalid_state`. Other outcomes need `reset_receipt: null` and a safe reason. Every update is kept in the history. A later failure never overwrites `resolved`. A `human_requested` escalation stays the outcome even when a reset completes (the receipt is added). Updates are idempotent by `operation_id`.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Tickets/TicketOutcomeTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Tests.Tickets;

public sealed class TicketOutcomeTests
{
    [Fact]
    public async Task RecordOutcome_EscalatedWithSafeReason_Returns200WithContractFields()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "escalated", "verification_exhausted");
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "outcome", "reason_code", "recovery_id", "reset_receipt", "ticket_id" }, json.PropertyNames());
        Assert.Equal("escalated", json.Text("outcome"));
        Assert.Equal("verification_exhausted", json.Text("reason_code"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("reset_receipt").ValueKind);
    }

    [Theory]
    [InlineData("resolved", "reset_completed", null)]          // resolved without a receipt
    [InlineData("resolved", "human_requested", "rcpt_x")]      // resolved with the wrong reason
    [InlineData("escalated", "human_requested", "rcpt_x")]     // a receipt on a non-resolved outcome
    [InlineData("escalated", "because_i_said_so", null)]       // unknown reason
    [InlineData("closed", "caller_cancelled", null)]           // unknown outcome
    [InlineData("open", "caller_cancelled", null)]             // "open" cannot be set
    public async Task RecordOutcome_MalformedUpdate_Returns400InvalidRequest(string outcome, string reason, string? receipt)
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, outcome, reason, receipt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task RecordOutcome_ResetReceiptFieldMissing_Returns400InvalidRequest()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());

        HttpResponseMessage response = await journey.Service.PostJsonAsync(
            $"/v1/tickets/{ticketId}/outcome", new { outcome = "cancelled", reason_code = "caller_cancelled", operation_id = "o-1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RecordOutcome_ResolvedWithFabricatedReceipt_Returns409InvalidState()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, _) = await journey.CompleteResetAsync();
        string ticketId = await journey.CreateTicketAsync(recoveryId);

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "resolved", "reset_completed", "rcpt_000000000000000000000000");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task RecordOutcome_ResolvedWithReceiptOfAnotherRecovery_Returns409InvalidState()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (_, string bobReceipt) = await journey.CompleteResetAsync("bob");
        string aliceTicket = await journey.CreateTicketAsync(await journey.StartAsync("alice"));

        HttpResponseMessage response = await journey.RecordOutcomeAsync(aliceTicket, "resolved", "reset_completed", bobReceipt);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task RecordOutcome_ResolvedWithValidReceipt_Returns200Resolved()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string receipt) = await journey.CompleteResetAsync();
        string ticketId = await journey.CreateTicketAsync(recoveryId);

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "resolved", "reset_completed", receipt);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("resolved", json.Text("outcome"));
        Assert.Equal(receipt, json.Text("reset_receipt"));
        Assert.Equal("reset_completed", json.Text("reason_code"));
    }

    [Fact]
    public async Task RecordOutcome_StaleFailureAfterResolved_DoesNotOverwriteSuccess()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string receipt) = await journey.CompleteResetAsync();
        string ticketId = await journey.CreateTicketAsync(recoveryId);
        await journey.RecordOutcomeAsync(ticketId, "resolved", "reset_completed", receipt);

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "cancelled", "call_dropped");
        TicketRecord? stored = await factory.Store.GetAsync<TicketRecord>(ticketId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("resolved", (await response.ReadJsonAsync()).Text("outcome"));
        Assert.Equal(2, stored?.History.Count);
        Assert.False(stored?.History[1].ChangedOutcome);
    }

    [Fact]
    public async Task RecordOutcome_ResolvedAfterCallDropped_ReplacesWithConfirmedSuccess()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string receipt) = await journey.CompleteResetAsync();
        string ticketId = await journey.CreateTicketAsync(recoveryId);
        await journey.RecordOutcomeAsync(ticketId, "cancelled", "call_dropped");

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "resolved", "reset_completed", receipt);

        Assert.Equal("resolved", (await response.ReadJsonAsync()).Text("outcome"));
    }

    [Fact]
    public async Task RecordOutcome_ResolvedAfterHumanRequested_KeepsEscalationAndAddsReceipt()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        (string recoveryId, string receipt) = await journey.CompleteResetAsync();
        string ticketId = await journey.CreateTicketAsync(recoveryId);
        await journey.RecordOutcomeAsync(ticketId, "escalated", "human_requested");

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "resolved", "reset_completed", receipt);
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal("escalated", json.Text("outcome"));
        Assert.Equal("human_requested", json.Text("reason_code"));
        Assert.Equal(receipt, json.Text("reset_receipt"));
    }

    [Fact]
    public async Task RecordOutcome_CancelledAfterHumanRequested_KeepsHumanRequest()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());
        await journey.RecordOutcomeAsync(ticketId, "escalated", "human_requested");

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "cancelled", "call_dropped");
        JsonElement json = await response.ReadJsonAsync();

        Assert.Equal("escalated", json.Text("outcome"));
        Assert.Equal("human_requested", json.Text("reason_code"));
    }

    [Fact]
    public async Task RecordOutcome_SameOperationRetried_ReplaysWithoutNewHistoryEntry()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());
        HttpResponseMessage first = await journey.RecordOutcomeAsync(ticketId, "pending", "completion_unknown", operationId: "o-1");

        HttpResponseMessage retry = await journey.RecordOutcomeAsync(ticketId, "pending", "completion_unknown", operationId: "o-1");
        TicketRecord? stored = await factory.Store.GetAsync<TicketRecord>(ticketId);

        Assert.Equal(await first.ReadTextAsync(), await retry.ReadTextAsync());
        Assert.Single(stored?.History ?? []);
    }

    [Fact]
    public async Task RecordOutcome_SameOperationDifferentBody_Returns409IdempotencyConflict()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());
        await journey.RecordOutcomeAsync(ticketId, "pending", "completion_unknown", operationId: "o-1");

        HttpResponseMessage response = await journey.RecordOutcomeAsync(ticketId, "cancelled", "caller_cancelled", operationId: "o-1");

        Assert.Equal("idempotency_conflict", (await response.ReadJsonAsync()).ErrorCode());
    }

    [Fact]
    public async Task RecordOutcome_SeveralUpdates_HistoryKeepsEveryUpdateInOrder()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string ticketId = await journey.CreateTicketAsync(await journey.StartAsync());
        await journey.RecordOutcomeAsync(ticketId, "pending", "completion_unknown");
        await journey.RecordOutcomeAsync(ticketId, "escalated", "dependency_unavailable");
        await journey.RecordOutcomeAsync(ticketId, "cancelled", "caller_cancelled");

        TicketRecord? stored = await factory.Store.GetAsync<TicketRecord>(ticketId);

        Assert.Equal(new[] { "pending", "escalated", "cancelled" }, stored?.History.Select(entry => entry.Outcome).ToArray());
        Assert.Equal("cancelled", stored?.Outcome);
    }

    [Fact]
    public async Task CreateTicket_AfterEscalation_ReturnsCurrentOutcomeWithoutReopening()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        string recoveryId = await journey.StartAsync();
        string ticketId = await journey.CreateTicketAsync(recoveryId);
        await journey.RecordOutcomeAsync(ticketId, "escalated", "browser_unavailable");

        HttpResponseMessage response = await journey.Service.PostJsonAsync("/v1/tickets", new { recovery_id = recoveryId, operation_id = "t-op-again" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("escalated", (await response.ReadJsonAsync()).Text("outcome"));
    }

    [Fact]
    public async Task RecordOutcome_UnknownTicket_Returns404NotFound()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);

        HttpResponseMessage response = await journey.RecordOutcomeAsync("tkt_doesnotexist", "cancelled", "caller_cancelled");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Tickets.TicketOutcomeTests"`
Expected: `failed: 20` (route missing).

- [ ] **Step 3: Add the route**

In `src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs`, add after `tickets.MapPost("", CreateTicketAsync);`:

```csharp
        tickets.MapPost("/{id}/outcome", RecordOutcomeAsync);
```

and add this handler after `CreateTicketAsync`:

```csharp

    private static async Task<ApiResult> RecordOutcomeAsync(string id, HttpRequest http, TicketService tickets) =>
        await MockJson.ReadAsync<TicketOutcomeRequest>(http) is { } request
            ? await tickets.RecordOutcomeAsync(id, request)
            : ApiResult.InvalidRequest();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Tickets.TicketOutcomeTests"`
Expected: `Passed!`, `succeeded: 20`, `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs tests/VoiceReset.Mocks.Tests/Tickets/TicketOutcomeTests.cs
git commit -m "feat(mocks): add ticket outcome updates with receipt validation"
```

---

### Task 17: Browser boundary: CORS, security headers, rate limits

**Files:**
- Create: `src/VoiceReset.Mocks/Shared/SecurityHeaders.cs`
- Create: `src/VoiceReset.Mocks/Shared/RateLimits.cs`
- Modify: `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs`
- Modify: `src/VoiceReset.Mocks/Program.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Shared/BrowserBoundaryTests.cs`

**Rules.** Only the four browser routes (`/v1/policy`, `/v1/password/validate`, `/v1/resets`, `GET /v1/reset-operations/{id}`) allow CORS, and only from `Mocks:AllowedCorsOrigin`. Service routes never answer CORS. Every response carries strict security headers (set in `OnStarting`, so error responses get them too) and `Cache-Control: no-store` (pages show codes and links). All `/v1` routes share a per-client-IP limit of 300 requests per minute (the "namespace rate limit"); a rejection is the contract's `429 throttled` with `Retry-After`. On App Service the app setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes `RemoteIpAddress` the real client IP (set in the deployment step; documented in `mock-services.md`). The limiter lives in memory per instance; that is a known limitation.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Shared/BrowserBoundaryTests.cs`:

```csharp
using System.Net;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Tests.Shared;

public sealed class BrowserBoundaryTests
{
    [Theory]
    [InlineData("/v1/resets", "POST")]
    [InlineData("/v1/password/validate", "POST")]
    [InlineData("/v1/reset-operations/op-1", "GET")]
    public async Task Preflight_AllowedOriginOnBrowserRoute_AllowsThatOrigin(string path, string method)
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await SendPreflightAsync(client, path, method, MocksFactory.AllowedOrigin);

        Assert.Equal(MocksFactory.AllowedOrigin, AllowOrigin(response));
    }

    [Fact]
    public async Task Preflight_OtherOrigin_GetsNoCorsHeader()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await SendPreflightAsync(client, "/v1/resets", "POST", "https://evil.example");

        Assert.Null(AllowOrigin(response));
    }

    [Fact]
    public async Task Preflight_AllowedOriginOnServiceRoute_GetsNoCorsHeader()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await SendPreflightAsync(client, "/v1/recoveries", "POST", MocksFactory.AllowedOrigin);

        Assert.Null(AllowOrigin(response));
    }

    [Fact]
    public async Task GetPolicy_FromAllowedOrigin_ReturnsAllowOriginHeader()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/v1/policy");
        request.Headers.Add("Origin", MocksFactory.AllowedOrigin);

        HttpResponseMessage response = await client.SendAsync(request, TestApi.Ct);

        Assert.Equal(MocksFactory.AllowedOrigin, AllowOrigin(response));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/v1/policy")]
    [InlineData("/v1/recoveries/rec_x")]
    public async Task AnyResponse_Always_CarriesSecurityHeaders(string path)
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path, TestApi.Ct);

        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", string.Join(",", response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("frame-ancestors 'none'", string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Api_MoreThanThePerMinuteLimit_Returns429ThrottledWithRetryAfter()
    {
        using MocksFactory factory = new();
        HttpClient client = factory.CreateClient();
        await Task.WhenAll(Enumerable.Range(0, RateLimits.ApiPermitsPerMinute).Select(_ => client.GetAsync("/v1/policy", TestApi.Ct)));

        HttpResponseMessage response = await client.GetAsync("/v1/policy", TestApi.Ct);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal("throttled", (await response.ReadJsonAsync()).ErrorCode());
        Assert.NotNull(response.Headers.RetryAfter);
    }

    private static async Task<HttpResponseMessage> SendPreflightAsync(HttpClient client, string path, string method, string origin)
    {
        using HttpRequestMessage request = new(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return await client.SendAsync(request, TestApi.Ct);
    }

    private static string? AllowOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? values) ? string.Join(",", values) : null;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Shared.BrowserBoundaryTests"`
Expected: build error `CS0103: The name 'RateLimits' does not exist in the current context`.

- [ ] **Step 3: Write the security headers middleware**

`src/VoiceReset.Mocks/Shared/SecurityHeaders.cs`:

```csharp
namespace VoiceReset.Mocks.Shared;

/// <summary>
/// Strict headers on every response. The inbox pages have no inline script or style,
/// so the CSP can forbid both. Set in OnStarting, so error responses get them too.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                IHeaderDictionary headers = context.Response.Headers;
                headers.ContentSecurityPolicy =
                    "default-src 'none'; style-src 'self'; img-src 'self' data:; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers.CacheControl = "no-store"; // pages and answers can show codes and links
                return Task.CompletedTask;
            });
            await next(context);
        });
}
```

- [ ] **Step 4: Write the rate limits**

`src/VoiceReset.Mocks/Shared/RateLimits.cs`:

```csharp
using System.Threading.RateLimiting;

namespace VoiceReset.Mocks.Shared;

/// <summary>
/// In-memory, per-instance rate limits by client IP. With
/// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (App Service) the IP is the real client.
/// </summary>
public static class RateLimits
{
    public const string Api = "api";
    public const string InboxLogin = "inbox-login";
    public const int ApiPermitsPerMinute = 300;
    public const int LoginAttemptsPerMinute = 5;

    public static IServiceCollection AddMockRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Api, context =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => PerMinute(ApiPermitsPerMinute)));

            // Only sign-in attempts (POST) count; showing the login page does not.
            options.AddPolicy(InboxLogin, context => HttpMethods.IsPost(context.Request.Method)
                ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => PerMinute(LoginAttemptsPerMinute))
                : RateLimitPartition.GetNoLimiter(ClientKey(context)));

            options.OnRejected = async (rejected, cancellationToken) =>
            {
                int seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : 60;
                await ApiResult.Throttled(Math.Max(1, seconds)).ExecuteAsync(rejected.HttpContext);
            };
        });

    private static FixedWindowRateLimiterOptions PerMinute(int permits) =>
        new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
```

- [ ] **Step 5: Apply CORS and rate limits to the route groups**

In `src/VoiceReset.Mocks/Issuer/IssuerEndpoints.cs`, replace

```csharp
        RouteGroupBuilder service = app.MapGroup("/v1")
            .AddEndpointFilter<ServiceAuthFilter>();
```

with

```csharp
        RouteGroupBuilder service = app.MapGroup("/v1")
            .AddEndpointFilter<ServiceAuthFilter>()
            .RequireRateLimiting(RateLimits.Api);
```

and replace

```csharp
        RouteGroupBuilder browser = app.MapGroup("/v1");
```

with

```csharp
        RouteGroupBuilder browser = app.MapGroup("/v1")
            .RequireCors(ResetFormCorsPolicy)
            .RequireRateLimiting(RateLimits.Api);
```

In `src/VoiceReset.Mocks/Tickets/TicketEndpoints.cs`, replace

```csharp
        RouteGroupBuilder tickets = app.MapGroup("/v1/tickets")
            .AddEndpointFilter<ServiceAuthFilter>();
```

with

```csharp
        RouteGroupBuilder tickets = app.MapGroup("/v1/tickets")
            .AddEndpointFilter<ServiceAuthFilter>()
            .RequireRateLimiting(RateLimits.Api);
```

- [ ] **Step 6: Wire CORS, headers and rate limiting in `Program.cs`**

Add `using Microsoft.AspNetCore.Cors.Infrastructure;` to the `using` lines.

Insert this block directly before `WebApplication app = builder.Build();`:

```csharp
// Browser boundary: only the registered reset form origin may call the browser routes.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<MocksOptions>>((cors, mocks) => cors.AddPolicy(IssuerEndpoints.ResetFormCorsPolicy, policy => policy
        .WithOrigins(mocks.Value.AllowedCorsOrigin)
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type", "Authorization")));
builder.Services.AddMockRateLimits();

```

Replace the line

```csharp
app.UseExceptionHandler(errorApp => errorApp.Run(context => ApiResult.DependencyUnavailable().ExecuteAsync(context)));
```

with

```csharp
app.UseExceptionHandler(errorApp => errorApp.Run(context => ApiResult.DependencyUnavailable().ExecuteAsync(context)));
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Shared.BrowserBoundaryTests"`
Expected: `Passed!`, `succeeded: 10`, `failed: 0`.

- [ ] **Step 8: Run all tests (the limits must not break earlier tests)**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests`
Expected: `Passed!`, `failed: 0` (Table Storage tests skipped).

- [ ] **Step 9: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Shared/BrowserBoundaryTests.cs
git commit -m "feat(mocks): restrict cors, add security headers and rate limits"
```

---

### Task 18: Mock inbox sign-in (`/inbox/login`)

**Files:**
- Create: `src/VoiceReset.Mocks/Inbox/InboxAuth.cs`
- Create: `src/VoiceReset.Mocks/Pages/_ViewImports.cshtml`
- Create: `src/VoiceReset.Mocks/Pages/_ViewStart.cshtml`
- Create: `src/VoiceReset.Mocks/Pages/Shared/_Layout.cshtml`
- Create: `src/VoiceReset.Mocks/Pages/Inbox/Login.cshtml`
- Create: `src/VoiceReset.Mocks/Pages/Inbox/Login.cshtml.cs`
- Create: `src/VoiceReset.Mocks/wwwroot/inbox.css`
- Modify: `src/VoiceReset.Mocks/Program.cs` (full replacement, final version)
- Create: `tests/VoiceReset.Mocks.Tests/Inbox/InboxClient.cs`
- Test: `tests/VoiceReset.Mocks.Tests/Inbox/InboxLoginTests.cs`

**How it works.** A plain HTML form (no JavaScript) posts username + inbox password. `SyntheticUsers.CheckInboxLogin` compares SHA-256 hashes in constant time (an unknown username costs the same). Success sets the cookie `__Host-vr-inbox` (`HttpOnly`, `Secure`, `SameSite=Strict`, 2 hours, no sliding) holding only the normalised username. Razor Pages validate the antiforgery token on every POST. Sign-in attempts (POST only) are limited to 5 per minute per client IP. The inbox cookie gives **no** access to any `/v1` route.

- [ ] **Step 1: Write the test helper**

`tests/VoiceReset.Mocks.Tests/Inbox/InboxClient.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoiceReset.Mocks.Tests.Inbox;

/// <summary>Uses the inbox like a browser: reads the form, sends the antiforgery token, keeps cookies.</summary>
public sealed partial class InboxClient(MocksFactory factory)
{
    public HttpClient Http { get; } = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    public async Task<HttpResponseMessage> LoginAsync(string username, string password)
    {
        string page = await Http.GetStringAsync("/inbox/login", TestApi.Ct);
        return await PostFormAsync("/inbox/login", page, new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
        });
    }

    public async Task<HttpResponseMessage> PostFormAsync(string url, string pageWithForm, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = AntiforgeryToken(pageWithForm);
        using FormUrlEncodedContent content = new(fields);
        return await Http.PostAsync(url, content, TestApi.Ct);
    }

    /// <summary>The inbox page as text, HTML entities decoded (Razor encodes some characters in attributes).</summary>
    public async Task<string> GetInboxHtmlAsync()
    {
        HttpResponseMessage response = await Http.GetAsync("/inbox", TestApi.Ct);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestApi.Ct));
    }

    public static string AntiforgeryToken(string html) => AntiforgeryPattern().Match(html).Groups[1].Value;

    public static string SetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? string.Join("\n", values) : "";

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();
}
```

- [ ] **Step 2: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Inbox/InboxLoginTests.cs`:

```csharp
using System.Net;

namespace VoiceReset.Mocks.Tests.Inbox;

public sealed class InboxLoginTests
{
    [Fact]
    public async Task LoginPage_Get_ShowsLabelledFormWithAntiforgeryToken()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);

        string html = await inbox.Http.GetStringAsync("/inbox/login", TestApi.Ct);

        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("<label for=\"username\">", html);
        Assert.Contains("<label for=\"password\">", html);
        Assert.Contains("id=\"login-button\"", html);
        Assert.NotEmpty(InboxClient.AntiforgeryToken(html));
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task Login_CorrectPassword_RedirectsToInboxWithSecureCookie()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);

        HttpResponseMessage response = await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);
        string cookie = InboxClient.SetCookieHeader(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/inbox", response.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__Host-vr-inbox=", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsErrorAndSetsNoInboxCookie()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);

        HttpResponseMessage response = await inbox.LoginAsync("alice", "wrong-password-123");
        string html = await response.Content.ReadAsStringAsync(TestApi.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"login-error\"", html);
        Assert.DoesNotContain("__Host-vr-inbox=", InboxClient.SetCookieHeader(response));
        Assert.DoesNotContain("wrong-password-123", html);
    }

    [Fact]
    public async Task Login_UnknownUser_ShowsTheSameErrorAsWrongPassword()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);

        HttpResponseMessage unknown = await inbox.LoginAsync("nobody.here", "wrong-password-123");
        HttpResponseMessage wrong = await inbox.LoginAsync("alice", "wrong-password-123");

        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        Assert.Contains("The username or password is not correct.", await unknown.Content.ReadAsStringAsync(TestApi.Ct));
        Assert.Contains("The username or password is not correct.", await wrong.Content.ReadAsStringAsync(TestApi.Ct));
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_Returns400()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = MocksFactory.AliceInboxPassword,
        });

        HttpResponseMessage response = await inbox.Http.PostAsync("/inbox/login", form, TestApi.Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_SixthAttemptWithinAMinute_Returns429()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => inbox.LoginAsync("alice", "wrong-password-123")));

        HttpResponseMessage response = await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Inbox_NotSignedIn_RedirectsToLogin()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);

        HttpResponseMessage response = await inbox.Http.GetAsync("/inbox", TestApi.Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/inbox/login", response.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InboxCookie_UsedOnServiceRoute_Returns401()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        HttpResponseMessage response = await inbox.Http.GetAsync("/v1/recoveries/rec_x", TestApi.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Inbox.InboxLoginTests"`
Expected: `failed: 7` or more (no pages yet: `/inbox/login` answers 404). `InboxCookie_UsedOnServiceRoute_Returns401` may already pass.

- [ ] **Step 4: Write the inbox authentication setup**

`src/VoiceReset.Mocks/Inbox/InboxAuth.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;

namespace VoiceReset.Mocks.Inbox;

/// <summary>
/// Cookie sign-in for the mock inbox only. The cookie holds the normalised username
/// and nothing else; it never authorizes any /v1 route.
/// </summary>
public static class InboxAuth
{
    public const string Scheme = "inbox";

    public static IServiceCollection AddInboxAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.LoginPath = "/inbox/login";
            options.Cookie.Name = "__Host-vr-inbox"; // __Host- = Secure, Path=/, no Domain
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(2);
            options.SlidingExpiration = false;
        });
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }
}
```

- [ ] **Step 5: Write the Razor Pages plumbing and the stylesheet**

`src/VoiceReset.Mocks/Pages/_ViewImports.cshtml`:

```cshtml
@using VoiceReset.Mocks.Storage
@namespace VoiceReset.Mocks.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

`src/VoiceReset.Mocks/Pages/_ViewStart.cshtml`:

```cshtml
@{
    Layout = "_Layout";
}
```

`src/VoiceReset.Mocks/Pages/Shared/_Layout.cshtml`:

```cshtml
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    @await RenderSectionAsync("Head", required: false)
    <title>@ViewData["Title"] - Mock recovery inbox</title>
    <link rel="icon" href="data:,">
    <link rel="stylesheet" href="~/inbox.css">
</head>
<body>
    <header>
        <p class="banner" id="synthetic-banner">Mock recovery inbox. Synthetic test data only.</p>
    </header>
    <main id="main">
        @RenderBody()
    </main>
</body>
</html>
```

(`<link rel="icon" href="data:,">` avoids a failing `/favicon.ico` request, so the browser console stays clean.)

`src/VoiceReset.Mocks/wwwroot/inbox.css`:

```css
body {
    font-family: system-ui, sans-serif;
    max-width: 48rem;
    margin: 0 auto;
    padding: 1rem;
    line-height: 1.5;
    color: #1a1a1a;
    background: #ffffff;
}

.banner {
    background: #fff4ce;
    padding: 0.5rem 1rem;
    border-radius: 4px;
}

label {
    display: block;
    font-weight: 600;
    margin-top: 0.75rem;
}

input {
    font-size: 1rem;
    padding: 0.4rem;
    width: 100%;
    max-width: 20rem;
}

button {
    margin-top: 1rem;
    font-size: 1rem;
    padding: 0.4rem 1rem;
}

#login-error {
    color: #a4262c;
    font-weight: 600;
}

#message-list {
    list-style: none;
    padding: 0;
}

#message-list > li {
    border: 1px solid #c8c8c8;
    border-radius: 4px;
    padding: 0.75rem 1rem;
    margin-bottom: 0.75rem;
}

#message-list h2 {
    font-size: 1.1rem;
    margin: 0 0 0.25rem;
}

[data-testid="verification-code"] {
    font-size: 1.5rem;
    letter-spacing: 0.2em;
}
```

- [ ] **Step 6: Write the login page**

`src/VoiceReset.Mocks/Pages/Inbox/Login.cshtml`:

```cshtml
@page
@model VoiceReset.Mocks.Pages.Inbox.LoginModel
@{
    ViewData["Title"] = "Sign in";
}
<h1 id="login-title">Sign in to the mock recovery inbox</h1>

@if (Model.ErrorMessage is not null)
{
    <p id="login-error" role="alert">@Model.ErrorMessage</p>
}

<form method="post" id="login-form" aria-labelledby="login-title">
    <label for="username">Username</label>
    <input id="username" name="Username" type="text" autocomplete="username" required value="@Model.Username">

    <label for="password">Inbox password</label>
    <input id="password" name="Password" type="password" autocomplete="current-password" required>

    <button type="submit" id="login-button">Sign in</button>
</form>
```

`src/VoiceReset.Mocks/Pages/Inbox/Login.cshtml.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Inbox;
using VoiceReset.Mocks.Shared;

namespace VoiceReset.Mocks.Pages.Inbox;

/// <summary>Inbox sign-in. Same error for unknown user and wrong password; attempts are rate-limited.</summary>
[EnableRateLimiting(RateLimits.InboxLogin)]
public sealed class LoginModel(SyntheticUsers users, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        MockUserOptions? user = users.CheckInboxLogin(Username, Password);
        Password = ""; // never rendered back
        if (user is null)
        {
            MockLog.InboxLoginFailed(logger);
            ErrorMessage = "The username or password is not correct.";
            return Page();
        }

        ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, SyntheticUsers.Normalize(user.Username))], InboxAuth.Scheme);
        await HttpContext.SignInAsync(InboxAuth.Scheme, new ClaimsPrincipal(identity));
        return RedirectToPage("/Inbox/Index");
    }
}
```

`/Inbox/Index` does not exist until Task 19; `RedirectToPage` to a missing page fails at run time, so create this minimal placeholder now (Task 19 replaces both files):

`src/VoiceReset.Mocks/Pages/Inbox/Index.cshtml`:

```cshtml
@page
@model VoiceReset.Mocks.Pages.Inbox.IndexModel
<h1 id="inbox-title">Inbox</h1>
```

`src/VoiceReset.Mocks/Pages/Inbox/Index.cshtml.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VoiceReset.Mocks.Pages.Inbox;

[Authorize]
public sealed class IndexModel : PageModel
{
}
```

- [ ] **Step 7: Replace `Program.cs` with its final version**

`src/VoiceReset.Mocks/Program.cs` (full file; it contains everything from the earlier tasks plus the inbox lines):

```csharp
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Options;
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Inbox;
using VoiceReset.Mocks.Issuer;
using VoiceReset.Mocks.Shared;
using VoiceReset.Mocks.Storage;
using VoiceReset.Mocks.Tickets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Time and configuration
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<MocksOptions>()
    .Bind(builder.Configuration.GetSection(MocksOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MocksOptions>, MocksOptionsValidator>();
builder.Services.AddOptions<FaultsOptions>()
    .Bind(builder.Configuration.GetSection(FaultsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<SyntheticUsers>();

// Storage: Table Storage when an endpoint is configured, otherwise in memory (data lost on restart).
string? tableEndpoint = builder.Configuration["Storage:TableEndpoint"];
if (string.IsNullOrWhiteSpace(tableEndpoint))
{
    builder.Services.AddSingleton<IMockStore, InMemoryMockStore>();
}
else
{
    // The one place that chooses the Azure credential: the app's own managed identity
    // in Azure, the developer's "az login" in Development. Never DefaultAzureCredential.
    TokenCredential credential = builder.Environment.IsDevelopment()
        ? new AzureCliCredential()
        : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
    builder.Services.AddAzureClients(clients =>
    {
        clients.AddTableServiceClient(new Uri(tableEndpoint));
        clients.UseCredential(credential);
    });
    builder.Services.AddSingleton<IMockStore, TableMockStore>();
}

// Issuer and tickets (stateless singletons; all state is in the store)
builder.Services.AddSingleton<ServiceAuth>();
builder.Services.AddSingleton<ResetCompletion>();
builder.Services.AddSingleton<RecoveryService>();
builder.Services.AddSingleton<VerificationService>();
builder.Services.AddSingleton<ResetLinkService>();
builder.Services.AddSingleton<ResetService>();
builder.Services.AddSingleton<TicketService>();

// Browser boundary: only the registered reset form origin may call the browser routes.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<MocksOptions>>((cors, mocks) => cors.AddPolicy(IssuerEndpoints.ResetFormCorsPolicy, policy => policy
        .WithOrigins(mocks.Value.AllowedCorsOrigin)
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type", "Authorization")));
builder.Services.AddMockRateLimits();

// Mock inbox: Razor Pages with its own cookie sign-in
builder.Services.AddRazorPages();
builder.Services.AddInboxAuthentication();

WebApplication app = builder.Build();

if (app.Services.GetRequiredService<IMockStore>() is TableMockStore tableStore)
{
    await tableStore.CreateTablesAsync();
}
else
{
    MockLog.UsingInMemoryStore(app.Logger);
}

// Any unexpected failure (for example storage down) is reported as the contract's 503.
app.UseExceptionHandler(errorApp => errorApp.Run(context => ApiResult.DependencyUnavailable().ExecuteAsync(context)));
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapIssuerEndpoints();
app.MapTicketEndpoints();
app.MapRazorPages();

await app.RunAsync();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Inbox.InboxLoginTests"`
Expected: `Passed!`, `succeeded: 8`, `failed: 0`.

- [ ] **Step 9: Commit**

```powershell
git add src/VoiceReset.Mocks tests/VoiceReset.Mocks.Tests/Inbox
git commit -m "feat(mocks): add mock inbox sign-in"
```

---

### Task 19: Mock inbox page (`/inbox`)

**Files:**
- Modify: `src/VoiceReset.Mocks/Pages/Inbox/Index.cshtml` (full replacement)
- Modify: `src/VoiceReset.Mocks/Pages/Inbox/Index.cshtml.cs` (full replacement)
- Test: `tests/VoiceReset.Mocks.Tests/Inbox/InboxPageTests.cs`

**How it works.** The page lists only the signed-in user's messages (the username comes from the cookie, never from the URL), newest first, at most 50. It reloads itself every 3 seconds with `<meta http-equiv="refresh" content="3">`: no JavaScript at all, so the console stays clean. Reset links open in a new tab with `rel="noopener noreferrer"` (no referrer leaks the token). Stable IDs and `data-testid` attributes let browser automation read it like a human would. There is no inbox JSON API.

- [ ] **Step 1: Write the failing tests**

`tests/VoiceReset.Mocks.Tests/Inbox/InboxPageTests.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace VoiceReset.Mocks.Tests.Inbox;

public sealed partial class InboxPageTests
{
    [Fact]
    public async Task Inbox_SignedInAsAlice_ShowsOnlyAlicesCode()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        InboxClient inbox = new(factory);
        await journey.StartAsync("alice");
        await journey.StartAsync("bob");
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        string html = await inbox.GetInboxHtmlAsync();

        Assert.Contains($"data-testid=\"verification-code\">{await journey.LatestCodeAsync("alice")}<", html);
        Assert.DoesNotContain($"data-testid=\"verification-code\">{await journey.LatestCodeAsync("bob")}<", html);
        Assert.Contains("Inbox for Alice Example", html);
    }

    [Fact]
    public async Task Inbox_SignedIn_RefreshesEveryThreeSecondsWithoutScript()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        string html = await inbox.GetInboxHtmlAsync();

        Assert.Contains("<meta http-equiv=\"refresh\" content=\"3\">", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task Inbox_TwoCodes_ShowsNewestFirst()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        InboxClient inbox = new(factory);
        await journey.StartAsync("alice");
        factory.Time.Advance(TimeSpan.FromSeconds(120));
        await journey.StartAsync("alice");
        string newestCode = await journey.LatestCodeAsync("alice");
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        string html = await inbox.GetInboxHtmlAsync();

        Assert.Equal(newestCode, FirstCodePattern().Match(html).Groups[1].Value);
    }

    [Fact]
    public async Task Inbox_ResetLink_OpensInNewTabWithoutReferrer()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        InboxClient inbox = new(factory);
        await journey.ReadyForResetAsync("alice");
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        string html = await inbox.GetInboxHtmlAsync();

        Assert.Contains($"data-testid=\"reset-link\" href=\"{MocksFactory.ResetBaseUrl}#token=", html);
        Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", html);
    }

    [Fact]
    public async Task Inbox_Messages_HaveStableIdsForAutomation()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        InboxClient inbox = new(factory);
        await journey.StartAsync("alice");
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);

        string html = await inbox.GetInboxHtmlAsync();

        Assert.Contains("id=\"inbox-title\"", html);
        Assert.Contains("<ol id=\"message-list\"", html);
        Assert.Contains("data-testid=\"message\" data-kind=\"verification_code\"", html);
        Assert.Contains("data-testid=\"message-time\"", html);
    }

    [Fact]
    public async Task Inbox_NoMessages_ShowsEmptyState()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);
        await inbox.LoginAsync("bob", MocksFactory.BobInboxPassword);

        string html = await inbox.GetInboxHtmlAsync();

        Assert.Contains("id=\"no-messages\"", html);
        Assert.DoesNotContain("id=\"message-list\"", html);
    }

    [Fact]
    public async Task Logout_SignedIn_EndsTheSession()
    {
        using MocksFactory factory = new();
        InboxClient inbox = new(factory);
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);
        string html = await inbox.GetInboxHtmlAsync();

        HttpResponseMessage logout = await inbox.PostFormAsync("/inbox?handler=Logout", html, new Dictionary<string, string>());
        HttpResponseMessage afterLogout = await inbox.Http.GetAsync("/inbox", TestApi.Ct);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
        Assert.Contains("/inbox/login", afterLogout.Headers.Location?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("data-testid=\"verification-code\">([0-9]{6})<")]
    private static partial Regex FirstCodePattern();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.Inbox.InboxPageTests"`
Expected: `failed: 7` (the placeholder page shows no messages, no refresh and no logout).

- [ ] **Step 3: Write the page model**

`src/VoiceReset.Mocks/Pages/Inbox/Index.cshtml.cs` (full replacement):

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoiceReset.Mocks.Configuration;
using VoiceReset.Mocks.Inbox;
using VoiceReset.Mocks.Storage;

namespace VoiceReset.Mocks.Pages.Inbox;

/// <summary>The signed-in user's messages. The username comes from the cookie, never from the request.</summary>
[Authorize]
public sealed class IndexModel(IMockStore store, SyntheticUsers users) : PageModel
{
    public const int MaxMessages = 50;

    public string DisplayName { get; private set; } = "";

    public IReadOnlyList<InboxMessage> Messages { get; private set; } = [];

    public async Task OnGetAsync()
    {
        string username = User.Identity?.Name ?? "";
        DisplayName = users.Find(username)?.DisplayName ?? username;
        Messages = await store.GetInboxAsync(username, MaxMessages);
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(InboxAuth.Scheme);
        return RedirectToPage("/Inbox/Login");
    }

    public static string Subject(InboxMessage message) =>
        message.Kind == InboxMessageKinds.VerificationCode ? "Your verification code" : "Your password reset link";

    public static string MachineTime(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    public static string ReadableTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Write the page**

`src/VoiceReset.Mocks/Pages/Inbox/Index.cshtml` (full replacement):

```cshtml
@page
@model VoiceReset.Mocks.Pages.Inbox.IndexModel
@{
    ViewData["Title"] = "Inbox";
}
@section Head {
    <meta http-equiv="refresh" content="3">
}
<h1 id="inbox-title">Inbox for @Model.DisplayName</h1>
<p id="refresh-note">This page refreshes every 3 seconds. The newest message is first.</p>

<form method="post" asp-page-handler="Logout" id="logout-form">
    <button type="submit" id="logout-button">Sign out</button>
</form>

@if (Model.Messages.Count == 0)
{
    <p id="no-messages">No messages yet.</p>
}
else
{
    <ol id="message-list" aria-labelledby="inbox-title">
        @foreach (InboxMessage message in Model.Messages)
        {
            <li id="message-@(message.MessageId)" data-testid="message" data-kind="@message.Kind">
                <article aria-labelledby="subject-@(message.MessageId)">
                    <h2 id="subject-@(message.MessageId)" data-testid="message-subject">@IndexModel.Subject(message)</h2>
                    <p>Received <time data-testid="message-time" datetime="@IndexModel.MachineTime(message.CreatedAt)">@IndexModel.ReadableTime(message.CreatedAt)</time></p>
                    @if (message.Code is not null)
                    {
                        <p>Your verification code is <strong data-testid="verification-code">@message.Code</strong>.
                            It is valid until <time datetime="@IndexModel.MachineTime(message.ExpiresAt)">@IndexModel.ReadableTime(message.ExpiresAt)</time>.</p>
                    }
                    @if (message.Link is not null)
                    {
                        <p><a data-testid="reset-link" href="@message.Link" target="_blank" rel="noopener noreferrer">Open the password reset form</a>
                            (single use, valid until <time datetime="@IndexModel.MachineTime(message.ExpiresAt)">@IndexModel.ReadableTime(message.ExpiresAt)</time>).</p>
                    }
                </article>
            </li>
        }
    </ol>
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-namespace "VoiceReset.Mocks.Tests.Inbox"`
Expected: `Passed!`, `succeeded: 15`, `failed: 0`.

- [ ] **Step 6: Check the page in a real browser (manual, 2 minutes)**

Run the app with test-like settings (PowerShell, from `solution/`; values are throwaway test values, not secrets; run `dotnet dev-certs https --trust` once if the HTTPS development certificate is missing):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Mocks__ServiceCredential = "local-service-credential-0123456789abcdef"
$env:Mocks__ResetBaseUrl = "https://localhost:7001/reset/"
$env:Mocks__AllowedCorsOrigin = "https://localhost:7001"
$env:Mocks__Users__0__Username = "alice"
$env:Mocks__Users__0__DisplayName = "Alice Example"
$env:Mocks__Users__0__RequiresUnlock = "true"
$env:Mocks__Users__0__InboxPassword = "local-inbox-password"
$env:Mocks__Users__0__InitialPassword = "First-Synthetic-Pass-11"
dotnet run --project src/VoiceReset.Mocks --urls https://localhost:7002
```

Open `https://localhost:7002/inbox/login`, sign in as `alice`, start a recovery from another terminal:

```powershell
Invoke-RestMethod -Method Post -Uri https://localhost:7002/v1/recoveries -Headers @{ Authorization = "Bearer local-service-credential-0123456789abcdef" } -ContentType "application/json" -Body '{"username":"alice","request_id":"manual-1"}'
```

Expected: within 3 seconds the code appears; the browser console (F12) shows no errors, warnings or logs. Stop the app with Ctrl+C and close the terminal (the environment variables disappear with it).

- [ ] **Step 7: Commit**

```powershell
git add src/VoiceReset.Mocks/Pages tests/VoiceReset.Mocks.Tests/Inbox/InboxPageTests.cs
git commit -m "feat(mocks): add self-refreshing mock inbox page"
```

---

### Task 20: No secret in any log line

**Files:**
- Test: `tests/VoiceReset.Mocks.Tests/LoggingTests.cs`

The factory captures every log line at `Debug` level and above, from our code and from ASP.NET Core. One full journey (code, link, validation, reset, ticket, inbox sign-in) must leave no code, token, password, inbox password or service credential in any line. This is the evidence for "never log bodies, codes, tokens, passwords".

- [ ] **Step 1: Write the test**

`tests/VoiceReset.Mocks.Tests/LoggingTests.cs`:

```csharp
using VoiceReset.Mocks.Tests.Inbox;

namespace VoiceReset.Mocks.Tests;

public sealed class LoggingTests
{
    [Fact]
    public async Task FullJourney_AllLogLines_ContainNoSecrets()
    {
        using MocksFactory factory = new();
        TestJourney journey = new(factory);
        InboxClient inbox = new(factory);
        string recoveryId = await journey.StartAsync();
        string code = await journey.LatestCodeAsync();
        await journey.VerifyAsync(recoveryId, code);
        await journey.IssueLinkAsync(recoveryId);
        string token = await journey.LatestTokenAsync();
        await journey.ValidateAsync(token, "weak-candidate-pw");
        string receipt = (await (await journey.ResetAsync(token, TestJourney.NewPassword)).ReadJsonAsync()).Text("reset_receipt");
        string ticketId = await journey.CreateTicketAsync(recoveryId);
        await journey.RecordOutcomeAsync(ticketId, "resolved", "reset_completed", receipt);
        await inbox.LoginAsync("alice", "wrong-inbox-password-1");
        await inbox.LoginAsync("alice", MocksFactory.AliceInboxPassword);
        await inbox.GetInboxHtmlAsync();

        string allLogs = string.Join("\n", factory.Logs.Lines);

        Assert.Contains("[1001]", allLogs); // proves that our own log lines were captured
        Assert.Contains("[1007]", allLogs);
        Assert.DoesNotContain(code, allLogs);
        Assert.DoesNotContain(token, allLogs);
        Assert.DoesNotContain(TestJourney.NewPassword, allLogs);
        Assert.DoesNotContain("weak-candidate-pw", allLogs);
        Assert.DoesNotContain(MocksFactory.AliceInboxPassword, allLogs);
        Assert.DoesNotContain("wrong-inbox-password-1", allLogs);
        Assert.DoesNotContain(MocksFactory.ServiceCredential, allLogs);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests --filter-class "VoiceReset.Mocks.Tests.LoggingTests"`
Expected: `Passed!`, `succeeded: 1`, `failed: 0`.

(If it fails, find the offending line in the failure output and remove the log call that wrote it. Do not weaken the test.)

- [ ] **Step 3: Prove the test catches a leak**

Temporarily add `logger.LogInformation("debug {Code}", code);` as the first line after `string code = Secrets.NewCode();` in `RecoveryService.StartAsync`, run the same command: expected `failed: 1`. Remove the line again and re-run: `failed: 0`.

- [ ] **Step 4: Commit**

```powershell
git add tests/VoiceReset.Mocks.Tests/LoggingTests.cs
git commit -m "test(mocks): prove that logs contain no codes, tokens or passwords"
```

---

### Task 21: Architecture document `docs/architecture/mock-services.md`

**Files:**
- Create: `solution/docs/architecture/mock-services.md`

The document must match the code at this commit (CLAUDE.md rule 5). It names configuration keys without values and lists the limitations honestly.

- [ ] **Step 1: Write the document**

`solution/docs/architecture/mock-services.md`:

````markdown
# Mock services (app-mocks)

The challenge expects the interviewer to supply a mock issuer, ticket service and
inbox. None was supplied, so we built them as a separate web app, **app-mocks**,
following [the mock contract](../../../docs/mock-contract.md). app-agent talks to it
only over HTTPS with the service credential; it has no access to the mocks' storage.

Code: `src/VoiceReset.Mocks`. Tests: `tests/VoiceReset.Mocks.Tests`.

## Parts

| Part | Folder | What it does |
|---|---|---|
| Mock issuer | `Issuer/` | Recoveries, codes, verification, reset links, password policy, resets, receipts |
| Mock ticket service | `Tickets/` | One ticket per recovery, outcome updates with history |
| Mock inbox | `Pages/Inbox/`, `Inbox/` | Sign-in page and a self-refreshing message list per synthetic user |
| Storage | `Storage/` | `IMockStore`: Azure Table Storage, or in memory when no endpoint is set |
| Shared | `Shared/` | Strict JSON, error envelope, service auth, rate limits, security headers, logs, `/health` |

## Endpoints

S = `Authorization: Bearer <service credential>`. B = reset token from the link.

| Method and path | Auth | Success | Main errors |
|---|---|---|---|
| `POST /v1/recoveries` | S | 202 `{recovery_id, status, verification_expires_at, attempts_remaining}` | 400, 401, 409 `idempotency_conflict`, 429 `throttled` |
| `POST /v1/recoveries/{id}/verify` + `Idempotency-Key` | S | 200 `{recovery_id, status: verified, attempts_remaining}` | 422 `verification_failed`, 409 `verification_exhausted` / `invalid_state` / `idempotency_conflict`, 410 `recovery_expired`, 404 |
| `POST /v1/recoveries/{id}/reset-link` | S | 200 `{recovery_id, status: link_issued, link_expires_at}` | 409 `invalid_state`, 410 `recovery_expired`, 404 |
| `GET /v1/recoveries/{id}` | S | 200 full status (8 fields) | 404 |
| `GET /v1/policy` | none | 200 `{policy_version, rules}` | |
| `POST /v1/password/validate` | B (body) | 200 `{valid, policy_version, violations}` | 401 `invalid_token`, 409 `token_used`, 410 `link_expired` |
| `POST /v1/resets` | B (body) | 200 succeeded or 202 pending `{operation_id, status, reset_receipt, unlock_status, reason_code}` | 422 `policy_violation`, 409 `token_used` / `idempotency_conflict`, 410, 401 |
| `GET /v1/reset-operations/{operation_id}` | S, or `Authorization: ResetToken <token>` | 200 with `recovery_id` | 404 (also for another token's operation), 401 |
| `POST /v1/tickets` | S | 201 `{ticket_id, recovery_id, outcome: open}`, 200 existing | 404, 409 `idempotency_conflict` |
| `POST /v1/tickets/{id}/outcome` | S | 200 `{ticket_id, recovery_id, outcome, reset_receipt, reason_code}` | 409 `invalid_state` (receipt), 400, 404 |
| `GET /health` | none | 200 `{status: ok, commit}` | |
| `GET/POST /inbox/login`, `GET /inbox` | inbox cookie | HTML pages | |

Every error is `{"error":{"code","message"}}` with a fixed message. Unexpected
failures (for example storage down) answer `503 dependency_unavailable`.

## Rules, in short

- **Strict JSON:** unknown, duplicate, missing or wrongly typed fields → `400 invalid_request`. Every field must be present (`reset_receipt` as `null` when empty).
- **Decoys:** an unknown username gets the same 202 envelope, its own throttling and the same failures, but no inbox message.
- **One active recovery per account** (username trimmed and lower-cased). A different start while one is active → `429` with `Retry-After`. At most one new code per account per 120 s. These counters live in storage, so a restart does not reset them.
- **Codes:** 6 digits chosen by the issuer (leading zeros kept), stored only as SHA-256 bound to the recovery, valid 120 s. Two completed wrong submissions exhaust the recovery. The result of each `Idempotency-Key` is saved in the same row as the counter: a retry is never counted twice.
- **Reset link:** 32 random bytes (base64url) per verified recovery, stored only as SHA-256, valid 10 minutes, delivered only to the inbox as `{ResetBaseUrl}#token=...`. Same `operation_id` → same answer; another `operation_id` → `409 invalid_state`.
- **Reset:** re-checks token, expiry, single use and policy. The token is reserved for one operation with an ETag write, so two parallel operations give one success and one `409 token_used`. An identical retry returns the recorded result, even after expiry. Success creates an opaque receipt `rcpt_...` and `unlock_status` (`unlocked` when the synthetic user requires an unlock).
- **Delayed completion:** with `Faults:ResetCompletionDelaySeconds` > 0 a reset answers `202 pending`; the first read after the delay completes it (no background job).
- **Tickets:** `resolved` only with the receipt the issuer created for this ticket's recovery; a later failure never overwrites `resolved`; a `human_requested` escalation stays (a completed reset only adds its receipt); every update is kept in the ticket history.

## Recovery status

```mermaid
stateDiagram-v2
    [*] --> awaiting_verification: POST /v1/recoveries
    awaiting_verification --> verified: correct code within 120 s
    awaiting_verification --> exhausted: second wrong code
    awaiting_verification --> expired: 120 s passed
    verified --> link_issued: POST reset-link
    verified --> expired: 120 s passed
    link_issued --> reset_pending: reset accepted
    link_issued --> expired: 10 minutes passed
    reset_pending --> completed: delay passed (checked on read)
    completed --> [*]
```

`expired` is computed from the deadlines when a recovery is read; it is never stored.
`reset_failed` is part of the contract but this mock has no failure path (see limitations).

## Storage

One storage account (`stvrmocks<suffix>`), reached with the app's managed identity
(role: Storage Table Data Contributor). Each row holds one record as JSON in a `Data`
column; keys are SHA-256 of the record key. Writes use ETags (optimistic concurrency).

| Table | Key | Holds |
|---|---|---|
| `accounts` | normalised username | active recovery, last code time, password hash |
| `recoveries` | recovery ID | code hash, attempts, verification results, link, reset, receipt |
| `idempotency` | action + key | recorded start / ticket-create results; reset operation → recovery |
| `tokens` | token SHA-256 | recovery ID |
| `tickets` | ticket ID | outcome, receipt, history |
| `inbox` | username hash / newest-first row key | inbox messages |

Without `Storage:TableEndpoint` the app uses an in-memory store and logs a warning.

## Mock inbox

- `/inbox/login`: plain HTML form, antiforgery token, constant-time password check,
  same error for unknown user and wrong password, 5 attempts per minute per IP.
- Cookie `__Host-vr-inbox`: `HttpOnly`, `Secure`, `SameSite=Strict`, 2 hours. It gives
  no access to any `/v1` route.
- `/inbox`: only the signed-in user's messages, newest first (max 50), reloaded every
  3 seconds with `<meta http-equiv="refresh">`. No JavaScript, so the console stays clean.
- Links open with `target="_blank" rel="noopener noreferrer"`.
- Stable IDs for automation: `inbox-title`, `message-list`, `no-messages`,
  `logout-button`, and `data-testid="message"`, `"message-subject"`, `"message-time"`,
  `"verification-code"`, `"reset-link"`.

## Configuration names (no values)

`Mocks__ServiceCredential` (secret), `Mocks__ResetBaseUrl`, `Mocks__AllowedCorsOrigin`,
`Mocks__Users__<n>__Username`, `__DisplayName`, `__RequiresUnlock`,
`__InboxPassword` (secret), `__InitialPassword` (secret), `Storage__TableEndpoint`,
`Faults__ResetCompletionDelaySeconds`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
(so rate limits see the real client IP). Secrets are App Service Key Vault references;
the app refuses to start if one is unresolved (`@Microsoft.KeyVault(...)` text).

## Security notes

- Codes, tokens, links and passwords are never logged (a test runs a full journey and
  searches every log line). Log messages are listed in `Shared/MockLog.cs`.
- No response to the service credential contains a code, token, link, password or
  inbox content.
- CORS allows only `Mocks:AllowedCorsOrigin`, and only on the four browser routes.
- Security headers on every response: strict CSP, `nosniff`, `no-referrer`,
  `X-Frame-Options: DENY`, `Cache-Control: no-store`; HSTS outside Development.
- Azure credential: managed identity in Azure, Azure CLI in Development; never
  `DefaultAzureCredential`.

## Known limitations

- **No reset failure path:** `reset_failed` / `status: failed` are never produced.
- **Verification and ticket-outcome idempotency keys are scoped per recovery/ticket**,
  not across the whole namespace. Reusing a key on another recovery is treated as new.
- **Code hashes are plain SHA-256.** A 6-digit code could be brute-forced from a stolen
  storage dump within its 120 s life; acceptable for synthetic data.
- **Small crash windows:** a crash between two writes can leave, for example, a
  started recovery without its recorded start result (a retry then gets `429`), or an
  issued link without its inbox message (the retry does not deliver it again).
- **Rate limits are in memory and per instance** (one instance in our plan).
- **Policy-rejected resets are not recorded**; a retry is evaluated again.
- **Decoy timing:** decoys skip the inbox write, so they answer slightly faster.
- The in-memory store loses everything on restart; it is for tests and local runs only.
````

- [ ] **Step 2: Check the links**

Open the file in a Markdown preview (VS Code: Ctrl+Shift+V) and check that the contract link and the Mermaid diagram render.

- [ ] **Step 3: Commit**

```powershell
git add docs/architecture/mock-services.md
git commit -m "docs(mocks): describe the mock services"
```

---

### Task 22: Full verification and the requirements checklist

**Files:**
- Modify: `solution/docs/submission/requirements-checklist.md` (section H)

- [ ] **Step 1: Build in Release (warnings are errors there)**

Run: `dotnet build VoiceReset.slnx -c Release`
Expected: `Build succeeded.`, `0 Warning(s)`, `0 Error(s)`. Fix any analyzer warning in the code (do not suppress it without a one-line reason).

- [ ] **Step 2: Run every mock test**

Run: `dotnet test --project tests/VoiceReset.Mocks.Tests`
Expected: `Passed!` with `failed: 0`, about `total: 226`, `succeeded: 219`, `skipped: 7` (the Azurite tests). If Azurite is running and `VOICERESET_TEST_AZURITE=1` is set, `skipped: 0`.

**Only if this step shows `failed: 0`, continue.** Otherwise fix the failures first.

- [ ] **Step 3: Tick section H of the checklist**

In `solution/docs/submission/requirements-checklist.md`, replace the whole section from `## H. Mock services we build (contract v1)` up to (not including) `## I. Video walkthrough (after feedback)` with:

```markdown
## H. Mock services we build (contract v1)

The interviewer didn't supply them, so we implement the candidate-facing API
([design](../architecture/mock-services.md)). Evidence is the test file named on each line.

- [x] `POST /v1/recoveries` (including decoy recoveries for unknown accounts). Evidence: [StartRecoveryTests](../../tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryTests.cs), [StartRecoveryRulesTests](../../tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryRulesTests.cs)
- [x] `POST /v1/recoveries/{id}/verify` (`Idempotency-Key`, attempt counting, expiry). Evidence: [VerifyTests](../../tests/VoiceReset.Mocks.Tests/Issuer/VerifyTests.cs)
- [x] `POST /v1/recoveries/{id}/reset-link` (token, 10-minute expiry, delivered to the inbox). Evidence: [ResetLinkTests](../../tests/VoiceReset.Mocks.Tests/Issuer/ResetLinkTests.cs)
- [x] `GET /v1/policy`. Evidence: [PasswordValidateTests](../../tests/VoiceReset.Mocks.Tests/Issuer/PasswordValidateTests.cs)
- [x] `POST /v1/password/validate`. Evidence: [PasswordValidateTests](../../tests/VoiceReset.Mocks.Tests/Issuer/PasswordValidateTests.cs), [PasswordPolicyTests](../../tests/VoiceReset.Mocks.Tests/Issuer/PasswordPolicyTests.cs)
- [x] `POST /v1/resets` (atomic token binding, receipts, `token_used`). Evidence: [ResetTests](../../tests/VoiceReset.Mocks.Tests/Issuer/ResetTests.cs), [DelayedResetTests](../../tests/VoiceReset.Mocks.Tests/Issuer/DelayedResetTests.cs)
- [x] `GET /v1/reset-operations/{operation_id}` (service auth or matching `ResetToken`). Evidence: [ResetOperationStatusTests](../../tests/VoiceReset.Mocks.Tests/Issuer/ResetOperationStatusTests.cs)
- [x] `GET /v1/recoveries/{id}`. Evidence: [RecoveryStatusTests](../../tests/VoiceReset.Mocks.Tests/Issuer/RecoveryStatusTests.cs)
- [x] `POST /v1/tickets` and `POST /v1/tickets/{id}/outcome`. Evidence: [CreateTicketTests](../../tests/VoiceReset.Mocks.Tests/Tickets/CreateTicketTests.cs), [TicketOutcomeTests](../../tests/VoiceReset.Mocks.Tests/Tickets/TicketOutcomeTests.cs)
- [x] The error format and HTTP codes from the contract; `Retry-After` on `429`. Evidence: [ApiResultTests](../../tests/VoiceReset.Mocks.Tests/Shared/ApiResultTests.cs), [MockJsonTests](../../tests/VoiceReset.Mocks.Tests/Shared/MockJsonTests.cs), [BrowserBoundaryTests](../../tests/VoiceReset.Mocks.Tests/Shared/BrowserBoundaryTests.cs)
- [x] Throttling: one active recovery per account; one new code per account per 120 s. Evidence: [StartRecoveryRulesTests](../../tests/VoiceReset.Mocks.Tests/Issuer/StartRecoveryRulesTests.cs)
- [x] Mock inbox: a self-refreshing web page with its own separate login (no API). Evidence: [InboxLoginTests](../../tests/VoiceReset.Mocks.Tests/Inbox/InboxLoginTests.cs), [InboxPageTests](../../tests/VoiceReset.Mocks.Tests/Inbox/InboxPageTests.cs)
- [ ] Pre-enrolled synthetic users (including one that needs an unlock). Code and tests are done ([OptionsValidationTests](../../tests/VoiceReset.Mocks.Tests/Configuration/OptionsValidationTests.cs), `RequiresUnlock` in [ResetTests](../../tests/VoiceReset.Mocks.Tests/Issuer/ResetTests.cs)); tick when the deployed app has its users configured (deploy step).

```

Leave every other section unchanged: sections D to G describe the whole system, and app-agent is not built yet.

- [ ] **Step 4: Commit**

```powershell
git add docs/submission/requirements-checklist.md
git commit -m "docs: tick mock service requirements with test evidence"
```

---

## Requirement coverage (self-review)

| Requirement (from the task / contract) | Task |
|---|---|
| All 10 candidate-facing routes | 6, 8, 9, 10, 11, 12, 14, 15, 16 |
| Service auth, constant time, one namespace | 6 (`ServiceAuth`) |
| Error envelope, extra fields, status codes, `Retry-After` | 2, 8, 11, 12, 17 |
| Strict JSON (unknown/duplicate/wrong type/missing) | 2, 6, 11, 16 |
| 6-digit issuer code, leading zeros, 120 s, hashed | 1, 6, 8 |
| Exactly two wrong attempts; replay not counted; conflict; invalid_state | 8 |
| Decoys indistinguishable, no delivery, same throttling | 7, 8, 10, 15 |
| One active recovery, 429 + Retry-After, one code per 120 s, restarts | 7, 10 |
| Link: 256-bit token, hash only, 10 min, inbox only, idempotent, 409 | 10 |
| Policy versioned, safe descriptions, no password echo | 11, 12 |
| Reset: rechecks, ETag reservation, concurrent `token_used`, retry after expiry, receipt, unlock | 12 |
| Delayed completion via `Faults:ResetCompletionDelaySeconds` | 13 |
| Operation status with S or matching ResetToken | 14 |
| Ticket: one per recovery, receipt validation, stale updates, human request, history | 15, 16 |
| Inbox: login (hashed constant-time, antiforgery, rate limit), own messages, newest first, 3 s refresh, link attributes, ids | 18, 19 |
| Synthetic users from config, validated on start, Key Vault reference check | 4 |
| CORS only for browser routes; security headers | 17 |
| No secrets in logs; `[LoggerMessage]` | 5, 20 |
| Table Storage + in-memory, ETag, credential choice | 3, 5 |
| `/health` with commit SHA | 4 |
| Docs + checklist | 21, 22 |

---

## Questions for the owner

1. **Verification and ticket-outcome idempotency keys are scoped per recovery / per ticket.** The contract says "namespace, action and key". Storing the result in the same row as the counter makes "never count twice" atomic and simple.
   *Recommended default:* keep per-recovery scope and document it (done in Task 21).
   *If you want namespace-wide keys:* add an `IdempotencyRecord` ("verify" + key → recovery ID) checked before `VerificationService` evaluates; about 15 lines plus 2 tests.

2. **No reset failure path.** Nothing produces `reset_failed` / `status: failed`, so app-agent cannot be tested end to end against a failed reset (its unit tests can still use fakes).
   *Recommended default:* leave it out (YAGNI), documented as a limitation.
   *If you want it:* add `Faults__ResetFailureUsernames` (list); `ResetCompletion` then sets `ResetFailed` with `reason_code: "dependency_unavailable"` instead of completing; ~20 lines + 3 tests; `00-overview` gets the new key.

3. **A sixth table, `tokens`,** was added (token hash → recovery ID). The task listed five tables.
   *Recommended default:* keep it; it is the simplest way to find a recovery from a token without scanning.
   *Alternative:* store it in `idempotency` with action "token"; fewer tables but a confusing name.

4. **In-memory fallback when `Storage:TableEndpoint` is missing.** Convenient for tests and local runs, but a misconfigured deployment would silently lose state on restart (only a warning log).
   *Recommended default:* keep the fallback + warning.
   *Stricter:* refuse to start outside Development when the endpoint is missing (3 lines in `Program.cs`).

5. **Store calls take no `CancellationToken`.** Deliberate: a mutation that has started finishes even if the client disconnects, so a retry finds consistent data. The research rule says "always pass a CancellationToken".
   *Recommended default:* keep, with the reason written in the plan and the code comments.
   *If you prefer the rule:* add `CancellationToken` to `IMockStore` and pass `CancellationToken.None` for writes; only reads would honour it.

6. **Rows store JSON in one `Data` column** instead of typed `ITableEntity` columns (the research example uses typed columns).
   *Recommended default:* keep JSON: one generic store for six record types, no enum issues, easy to explain.
   *Alternative:* typed entities per table; more code, readable rows in Storage Explorer.

7. **Password policy content:** 7 rules (`min_length_12`, `max_length_128`, `has_upper`, `has_lower`, `has_digit`, `not_contains_username`, `not_current_password`), version `2026-10-v1`.
   *Recommended default:* as listed. Changing rules only changes `PasswordPolicy.cs` and its tests; the reset form shows whatever `/v1/policy` returns.

8. **Rate limits:** 300 `/v1` requests per minute per client IP; 5 inbox sign-in attempts per minute per IP.
   *Recommended default:* as listed. If the interviewer's automated tester runs many parallel calls from one IP, raise the API limit (one constant).

9. **`human_requested` + later completed reset:** the ticket stays `escalated/human_requested` and gets the receipt added. Stale updates after `resolved` answer `200` with the unchanged ticket (not an error).
   *Recommended default:* as described (matches 00-overview §7).
   *Alternative:* answer `409 invalid_state` for updates that are ignored; app-agent would then need to treat that 409 as "already final".

10. **Ticket ID format `tkt_<recovery_id>`** (derived, so "one per recovery" needs no extra index). It reveals which recovery a ticket belongs to, but recovery IDs are not credentials and the response already contains `recovery_id`.
    *Recommended default:* keep. *Alternative:* random ticket IDs + an index row per recovery.

11. **Azurite tests are optional** (skipped unless `VOICERESET_TEST_AZURITE=1`). The Table store is otherwise only proven in Azure.
    *Recommended default:* run them once locally before deploying; add Azurite to CI later if a CI pipeline is built.

12. **Exhausted recoveries keep the status `exhausted`** after the 120 s window (they are not shown as `expired`), so reconciliation sees why it ended. They stop blocking new starts when the window ends.
    *Recommended default:* keep. *Alternative:* show `expired` after the window; one line in `RecoveryRecord.StatusAt`.

## Additions to 00-overview

- **Mocks storage tables** (§2 `Storage/`): `accounts`, `recoveries`, `idempotency`, `tokens`, `tickets`, `inbox`. The mocks' managed identity needs **Storage Table Data Contributor** on `stvrmocks<suffix>` (step 4).
- **Mocks `Shared/` folder** also holds `ServiceAuth`, `RateLimits`, `SecurityHeaders`, `HealthEndpoints` and `MockLog` (the overview lists a `Health/` folder only for app-agent).
- **App setting for app-mocks:** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (real client IP for rate limits; also needed by app-agent).
- **Package:** `Microsoft.Extensions.Azure` 1.14.1 is used by app-mocks too (`AddAzureClients`).
- **ID formats:** recovery `rec_<24 hex>`, ticket `tkt_<recovery id>`, receipt `rcpt_<24 hex>`, inbox message `msg_<24 hex>`. Policy version `2026-10-v1`.
- **Inbox cookie name:** `__Host-vr-inbox`.
- **Test command form:** `dotnet test --project tests/<Project> --filter-class "<Namespace.Class>"` (Microsoft Testing Platform; `global.json` has `"test": {"runner": "Microsoft.Testing.Platform"}`).
- **Optional local emulator:** Azurite for Table Storage tests (`VOICERESET_TEST_AZURITE=1`).
