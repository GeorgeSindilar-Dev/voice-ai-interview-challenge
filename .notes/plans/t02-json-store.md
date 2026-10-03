# T2: JSON State Store Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A tiny key/value store for JSON documents: in memory for tests and local runs, Blob Storage (container `state`) in Azure.

**Architecture:** `IJsonStore` (contract in `00-contracts.md`) with two implementations. `AddJsonStore` binds
`StorageOptions` and picks the implementation lazily when `IJsonStore` is first resolved: empty `Storage:BlobEndpoint`
→ `InMemoryJsonStore`, otherwise `BlobJsonStore` with a `BlobContainerClient` built from the endpoint and the shared
`TokenCredential` singleton (`DefaultAzureCredential`). Both stores use `JsonSerializerOptions.Web` (camelCase).

**Tech Stack:** .NET 10, System.Text.Json, Azure.Storage.Blobs 12.30.0, Azure.Identity 1.21.0, xUnit v3.

All commands run from `solution/`.

---

## File structure

| File | Responsibility |
|---|---|
| Modify `src/VoiceReset/VoiceReset.csproj` | Package references for Blob + Identity |
| Create `src/VoiceReset/Storage/IJsonStore.cs` | The interface |
| Create `src/VoiceReset/Storage/InMemoryJsonStore.cs` | Dictionary of JSON strings |
| Create `src/VoiceReset/Storage/BlobJsonStore.cs` | One blob per key |
| Create `src/VoiceReset/Storage/StorageOptions.cs` | `Storage:BlobEndpoint` |
| Create `src/VoiceReset/Storage/StorageServiceCollectionExtensions.cs` | `AddJsonStore`, credential singleton |
| Modify `src/VoiceReset/Program.cs`, `appsettings.json`, `appsettings.Development.json` | Wiring + empty endpoint |
| Create `tests/VoiceReset.Tests/Storage/InMemoryJsonStoreTests.cs` | 2 tests |

---

### Task 1: In-memory store (TDD)

**Files:**
- Create: `src/VoiceReset/Storage/IJsonStore.cs`, `src/VoiceReset/Storage/InMemoryJsonStore.cs`
- Test: `tests/VoiceReset.Tests/Storage/InMemoryJsonStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using VoiceReset.Storage;

namespace VoiceReset.Tests.Storage;

public sealed class InMemoryJsonStoreTests
{
    private sealed record Sample(string Name, int Count);

    [Fact]
    public async Task WriteThenRead_SameKey_ReturnsEqualValue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryJsonStore();

        // Act
        await store.WriteAsync("mock/state.json", new Sample("a", 1), ct);
        var read = await store.ReadAsync<Sample>("mock/state.json", ct);
        var missing = await store.ReadAsync<Sample>("mock/other.json", ct);

        // Assert
        Assert.Equal(new Sample("a", 1), read);
        Assert.Null(missing);
    }

    [Fact]
    public async Task ListKeys_WithPrefix_ReturnsOnlyMatchingKeysInOrder()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryJsonStore();
        await store.WriteAsync("sessions/b.json", new Sample("b", 2), ct);
        await store.WriteAsync("mock/state.json", new Sample("m", 0), ct);
        await store.WriteAsync("sessions/a.json", new Sample("a", 1), ct);

        // Act
        var keys = await store.ListKeysAsync("sessions/", ct);

        // Assert
        Assert.Collection(
            keys,
            key => Assert.Equal("sessions/a.json", key),
            key => Assert.Equal("sessions/b.json", key));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Storage.InMemoryJsonStoreTests"`
Expected: build error `CS0246: The type or namespace name 'InMemoryJsonStore' could not be found`.

- [ ] **Step 3: Write the interface and the in-memory store**

`src/VoiceReset/Storage/IJsonStore.cs`:

```csharp
namespace VoiceReset.Storage;

/// <summary>Stores JSON documents by key (for example "mock/state.json" or "sessions/{id}.json").</summary>
public interface IJsonStore
{
    /// <summary>Returns the document, or null when the key does not exist.</summary>
    Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class;

    /// <summary>Creates or replaces the document.</summary>
    Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class;

    /// <summary>Returns the keys that start with the prefix, in ordinal order.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct);
}
```

`src/VoiceReset/Storage/InMemoryJsonStore.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;

namespace VoiceReset.Storage;

/// <summary>
/// Keeps documents as JSON strings, so a read returns a copy, exactly like the blob store.
/// Used by tests and local runs without Azure.
/// </summary>
public sealed class InMemoryJsonStore : IJsonStore
{
    private readonly ConcurrentDictionary<string, string> _documents = new(StringComparer.Ordinal);

    public Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class =>
        Task.FromResult(_documents.TryGetValue(key, out var json)
            ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)
            : null);

    public Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class
    {
        _documents[key] = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. _documents.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)]);
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Storage.InMemoryJsonStoreTests"`
Expected: `Test run summary: Passed!` with total 2, failed 0.

- [ ] **Step 5: Commit**

```bash
git add src/VoiceReset/Storage tests/VoiceReset.Tests/Storage
git commit -m "feat(storage): add JSON store interface and in-memory store"
```

---

### Task 2: Blob store, options and registration

No automated test (needs Azure). It is exercised in Azure after T15; locally the in-memory store is used.

**Files:**
- Modify: `src/VoiceReset/VoiceReset.csproj`
- Create: `src/VoiceReset/Storage/BlobJsonStore.cs`, `src/VoiceReset/Storage/StorageOptions.cs`,
  `src/VoiceReset/Storage/StorageServiceCollectionExtensions.cs`
- Modify: `src/VoiceReset/Program.cs`, `src/VoiceReset/appsettings.json`, `src/VoiceReset/appsettings.Development.json`

- [ ] **Step 1: Add the package references** (versions are central in `Directory.Packages.props`)

`src/VoiceReset/VoiceReset.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="Azure.Identity" />
    <PackageReference Include="Azure.Storage.Blobs" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the blob store**

`src/VoiceReset/Storage/BlobJsonStore.cs`:

```csharp
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace VoiceReset.Storage;

/// <summary>One blob per key in the "state" container.</summary>
public sealed class BlobJsonStore(BlobContainerClient container) : IJsonStore
{
    private static readonly BlobUploadOptions s_uploadOptions = new()
    {
        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
    };

    public async Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            var download = await container.GetBlobClient(key).DownloadContentAsync(ct);
            return download.Value.Content.ToObjectFromJson<T>(JsonSerializerOptions.Web);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class =>
        await container.GetBlobClient(key)
            .UploadAsync(BinaryData.FromObjectAsJson(value, JsonSerializerOptions.Web), s_uploadOptions, ct);

    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct)
    {
        List<string> keys = [];
        await foreach (var blob in container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, ct))
        {
            keys.Add(blob.Name);
        }

        return keys;
    }
}
```

The SDK reports a missing blob as `RequestFailedException` with status 404; catching exactly that turns it into the
`null` the interface promises. Blob listing is already in ordinal (lexicographic) order.

- [ ] **Step 3: Write the options and the registration**

`src/VoiceReset/Storage/StorageOptions.cs`:

```csharp
namespace VoiceReset.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>For example https://account.blob.core.windows.net/. Empty → in-memory store.</summary>
    public string BlobEndpoint { get; set; } = "";

    public static bool IsValid(StorageOptions options) =>
        options.BlobEndpoint.Length == 0 || Uri.TryCreate(options.BlobEndpoint, UriKind.Absolute, out _);
}
```

`src/VoiceReset/Storage/StorageServiceCollectionExtensions.cs`:

```csharp
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace VoiceReset.Storage;

public static class StorageServiceCollectionExtensions
{
    public const string ContainerName = "state";

    /// <summary>
    /// Registers IJsonStore (Blob Storage when Storage:BlobEndpoint is set, otherwise in memory)
    /// and the shared TokenCredential (managed identity in Azure, az login locally).
    /// </summary>
    public static IServiceCollection AddJsonStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(StorageOptions.IsValid, "Storage:BlobEndpoint must be empty or an absolute URL.")
            .ValidateOnStart();

        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton<IJsonStore>(CreateStore);
        return services;
    }

    private static IJsonStore CreateStore(IServiceProvider services)
    {
        var endpoint = services.GetRequiredService<IOptions<StorageOptions>>().Value.BlobEndpoint;
        if (endpoint.Length == 0)
        {
            return new InMemoryJsonStore();
        }

        var container = new BlobServiceClient(new Uri(endpoint), services.GetRequiredService<TokenCredential>())
            .GetBlobContainerClient(ContainerName);
        container.CreateIfNotExists();
        return new BlobJsonStore(container);
    }
}
```

`CreateIfNotExists` runs once, when the singleton is first resolved at startup (T3 loads the mock state before the
server starts). A DI factory can't await, and the SDK offers a real synchronous call, so this is not sync-over-async.

- [ ] **Step 4: Wire it up**

`src/VoiceReset/Program.cs`:

```csharp
using VoiceReset.Health;
using VoiceReset.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddJsonStore(builder.Configuration);

var app = builder.Build();

app.MapHealth();

await app.RunAsync();
```

(`TimeProvider.System` is registered here once; T3 and later depend on it and tests replace it with `FakeTimeProvider`.)

In both `src/VoiceReset/appsettings.json` and `src/VoiceReset/appsettings.Development.json` add, next to `"Logging"`:

```json
  "Storage": {
    "BlobEndpoint": ""
  }
```

(Azure sets `Storage__BlobEndpoint` as an app setting, T15.)

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build VoiceReset.slnx -c Release`
Expected: build succeeds, 0 warnings.

Run: `dotnet test --project tests/VoiceReset.Tests`
Expected: `Test run summary: Passed!` with total 7, failed 0 (5 health + 2 storage).

- [ ] **Step 6: Commit**

```bash
git add src/VoiceReset tests/VoiceReset.Tests
git commit -m "feat(storage): add blob JSON store and registration"
```

---

## Self-review

- Contract names: `IJsonStore`, `InMemoryJsonStore`, `BlobJsonStore(BlobContainerClient)`, `StorageOptions.BlobEndpoint`,
  `AddJsonStore(services, configuration)`, container `state`, web JSON defaults, shared `TokenCredential` ✓.
- Tests: round trip (plus missing key → null) and prefix listing ✓. Blob store untested by design ✓.
- No placeholders; every code step is complete.

## Questions

1. `CreateIfNotExists` at first resolve needs the identity to have permission to create containers (Storage Blob Data
   Contributor does). If T15's script creates the `state` container anyway, should we drop this call and require the
   container to exist?
2. Writes are last-writer-wins (no ETags). Fine because one app instance owns each key (one App Service instance);
   OK to state that as a known limitation in SETUP.md?

## Additions to contracts

- `StorageOptions.SectionName = "Storage"`, `StorageOptions.IsValid(StorageOptions)`.
- `StorageServiceCollectionExtensions.ContainerName = "state"`.
- `Program.cs` registers `TimeProvider.System` as a singleton (T2); tests override it with `FakeTimeProvider`.
- `Program.cs` ends with `await app.RunAsync();` (so startup code can `await` before it, T3).
