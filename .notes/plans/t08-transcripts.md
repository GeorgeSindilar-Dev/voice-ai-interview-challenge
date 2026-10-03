# T8 Transcripts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A masked transcript per call (a debug aid): codes, spoken passwords and links never reach storage.

**Architecture:** `TranscriptRecorder` (one per call, created by the voice session in T11) masks each turn with `TranscriptMasker.Mask` **when the turn is added**, so raw text is never kept. At call end, `ToDocument(endReason)` gives a `TranscriptDocument` that an `ITranscriptWriter` saves: `BlobTranscriptWriter` (container `transcripts`, blob `yyyy/MM/dd/<sessionId>.json`, create-only) or `NullTranscriptWriter` when `Storage:BlobEndpoint` is empty.

**Tech Stack:** .NET 10, `System.Text.RegularExpressions` (source-generated), Azure.Storage.Blobs 12.30.0, xUnit v3, `FakeTimeProvider`.

Rules: CLAUDE.md 4 (transcripts mask digit runs and anything after "password is"; never logged). Masking is best effort and documented as such (README: perfect redaction is not a guarantee). Commands run from `solution/`.

## Files

| File | Responsibility |
|---|---|
| `src/VoiceReset/Transcripts/TranscriptMasker.cs` | `Mask(text)` |
| `src/VoiceReset/Transcripts/TranscriptDocument.cs` | `TranscriptDocument`, `TranscriptTurn` |
| `src/VoiceReset/Transcripts/TranscriptRecorder.cs` | Collects masked turns for one call |
| `src/VoiceReset/Transcripts/TranscriptWriters.cs` | `ITranscriptWriter`, `BlobTranscriptWriter`, `NullTranscriptWriter` |
| `src/VoiceReset/Transcripts/TranscriptServiceCollectionExtensions.cs` | `AddTranscripts(configuration)` |
| `src/VoiceReset/Program.cs` | `builder.Services.AddTranscripts(builder.Configuration);` |
| `tests/VoiceReset.Tests/Transcripts/TranscriptMaskerTests.cs`, `TranscriptRecorderTests.cs` | Tests |

---

### Task 1: TranscriptMasker

- [ ] **Step 1: Write the failing test** — `tests/VoiceReset.Tests/Transcripts/TranscriptMaskerTests.cs`

```csharp
using VoiceReset.Transcripts;

namespace VoiceReset.Tests.Transcripts;

public sealed class TranscriptMaskerTests
{
    [Theory]
    [InlineData("my code is 0 4 7 1 1 2", "my code is [CODE]")]
    [InlineData("it's 047112.", "it's [CODE].")]
    [InlineData("zero four seven one one two", "[CODE]")]
    [InlineData("Oh four seven, double one two", "[CODE]")]
    [InlineData("my password is Summer2026!", "my password is [REDACTED]")]
    [InlineData("open https://app.example/reset/#token=abc123 now", "open [LINK] now")]
    [InlineData("I have 2 laptops", "I have 2 laptops")]
    [InlineData("Hello, I need to reset my password.", "Hello, I need to reset my password.")]
    [InlineData("one moment, oh I see", "one moment, oh I see")]
    public void Mask_Text_HidesCodesPasswordsAndLinks(string text, string expected)
    {
        // Act
        var masked = TranscriptMasker.Mask(text);

        // Assert
        Assert.Equal(expected, masked);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** — `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Transcripts.TranscriptMaskerTests"` → `error CS0103: The name 'TranscriptMasker' does not exist in the current context`.

- [ ] **Step 3: Implement** — `src/VoiceReset/Transcripts/TranscriptMasker.cs`

```csharp
using System.Text.RegularExpressions;

namespace VoiceReset.Transcripts;

/// <summary>
/// Masks secrets in one transcript turn before it is stored. Best effort: speech can be transcribed in many ways.
/// Order matters: links first (they contain digits), then "password is …" (rest of the turn), then digit runs.
/// </summary>
public static partial class TranscriptMasker
{
    private const int MinCodeDigits = 3;
    private const string Digit = "(?:[0-9]+|zero|oh|one|two|three|four|five|six|seven|eight|nine)";
    private const string Repeat = @"(?:(?:double|triple)\s+)?";

    public static string Mask(string text)
    {
        var masked = UrlPattern().Replace(text, "[LINK]");
        masked = PasswordPattern().Replace(masked, "$1 [REDACTED]");
        return DigitRunPattern().Replace(masked, MaskRun);
    }

    /// <summary>A run like "oh four seven, double one two" counts its digits; 3 or more → [CODE].</summary>
    private static string MaskRun(Match run)
    {
        var digits = 0;
        foreach (var word in RunSeparator().Split(run.Value.ToLowerInvariant()))
        {
            digits += word switch
            {
                "double" => 1,   // "double one" = two digits
                "triple" => 2,
                _ when char.IsAsciiDigit(word[0]) => word.Length,
                _ => 1,
            };
        }
        return digits >= MinCodeDigits ? "[CODE]" : run.Value;
    }

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"\b(password\s+is)\b.*", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"\b" + Repeat + Digit + @"(?:[\s,.\-]+" + Repeat + Digit + @")*\b", RegexOptions.IgnoreCase)]
    private static partial Regex DigitRunPattern();

    [GeneratedRegex(@"[\s,.\-]+")]
    private static partial Regex RunSeparator();
}
```

Why these cases hold: a run must start and end on a whole word (`\b`), so "someone", "ohio" or "Summer2026" never match a digit word; "one moment" and "oh I see" are runs of one digit and stay; "2 laptops" is one digit.

- [ ] **Step 4: Run the test** — same command → `Test run summary: Passed!`, `total: 9`, `failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add solution/src/VoiceReset/Transcripts/TranscriptMasker.cs solution/tests/VoiceReset.Tests/Transcripts/TranscriptMaskerTests.cs
git commit -m "feat: add transcript masking"
```

---

### Task 2: Recorder, document and writers

- [ ] **Step 1: Write the failing test** — `tests/VoiceReset.Tests/Transcripts/TranscriptRecorderTests.cs`

```csharp
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Transcripts;

namespace VoiceReset.Tests.Transcripts;

public sealed class TranscriptRecorderTests
{
    [Fact]
    public void ToDocument_AfterTurns_KeepsOrderAndMasks()
    {
        // Arrange
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var recorder = new TranscriptRecorder("s1", "browser", time);
        recorder.AddAgent("Hi, I'm an automated assistant.");
        time.Advance(TimeSpan.FromMilliseconds(1500));
        recorder.AddCaller("The code is zero four seven one one two");
        time.Advance(TimeSpan.FromSeconds(2));
        recorder.AddCaller("   ");
        recorder.AddAgent("Thanks.");

        // Act
        var doc = recorder.ToDocument("caller_hangup");

        // Assert
        TranscriptTurn[] expected =
        [
            new("agent", 0, "Hi, I'm an automated assistant."),
            new("caller", 1500, "The code is [CODE]"),
            new("agent", 3500, "Thanks."),
        ];
        Assert.Equal(expected, doc.Turns);
        Assert.Equal(("s1", "browser", "caller_hangup"), (doc.SessionId, doc.Channel, doc.EndReason));
        Assert.Equal(time.GetUtcNow(), doc.EndedAt);
    }
}
```

- [ ] **Step 2: Run, expect a compile failure** — `dotnet test --project tests/VoiceReset.Tests --filter-class "VoiceReset.Tests.Transcripts.TranscriptRecorderTests"` → `error CS0246: The type or namespace name 'TranscriptRecorder' could not be found`.

- [ ] **Step 3: `TranscriptDocument.cs`** (stored with System.Text.Json web defaults, camelCase)

```csharp
namespace VoiceReset.Transcripts;

public sealed record TranscriptTurn(string Role, long OffsetMs, string Text);   // Role: "caller" or "agent"; Text is already masked

public sealed record TranscriptDocument(
    string SessionId, string Channel, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string EndReason,
    IReadOnlyList<TranscriptTurn> Turns);
```

- [ ] **Step 4: `TranscriptRecorder.cs`**

```csharp
namespace VoiceReset.Transcripts;

/// <summary>Final caller/agent sentences of one call. Text is masked as it is added, so raw text is never kept.</summary>
public sealed class TranscriptRecorder(string sessionId, string channel, TimeProvider time)
{
    private readonly TimeProvider _time = time;   // stored, not captured (avoids CS9124 with the initializer below)
    private readonly DateTimeOffset _startedAt = time.GetUtcNow();
    private readonly List<TranscriptTurn> _turns = [];
    private readonly Lock _lock = new();   // caller and agent events can arrive on different threads

    public void AddCaller(string text) => Add("caller", text);

    public void AddAgent(string text) => Add("agent", text);

    public TranscriptDocument ToDocument(string endReason)
    {
        lock (_lock)
        {
            return new(sessionId, channel, _startedAt, _time.GetUtcNow(), endReason, [.. _turns]);
        }
    }

    private void Add(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        var offsetMs = (long)(_time.GetUtcNow() - _startedAt).TotalMilliseconds;
        var turn = new TranscriptTurn(role, offsetMs, TranscriptMasker.Mask(text));
        lock (_lock)
        {
            _turns.Add(turn);
        }
    }
}
```

- [ ] **Step 5: Run the test** — same command → `Test run summary: Passed!`, `total: 1`, `failed: 0`.

- [ ] **Step 6: `TranscriptWriters.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace VoiceReset.Transcripts;

public interface ITranscriptWriter
{
    Task SaveAsync(TranscriptDocument doc, CancellationToken ct);
}

/// <summary>Local runs and tests without Blob Storage: transcripts are not kept.</summary>
public sealed class NullTranscriptWriter : ITranscriptWriter
{
    public Task SaveAsync(TranscriptDocument doc, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// One blob per call: transcripts/yyyy/MM/dd/&lt;sessionId&gt;.json (UTC start date). Create-only: an existing blob is
/// never overwritten (Azure returns 409; the caller logs and continues).
/// </summary>
public sealed class BlobTranscriptWriter(BlobContainerClient container) : ITranscriptWriter
{
    public async Task SaveAsync(TranscriptDocument doc, CancellationToken ct)
    {
        var name = $"{doc.StartedAt.UtcDateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}/{doc.SessionId}.json";
        var content = BinaryData.FromObjectAsJson(doc, JsonSerializerOptions.Web);
        await container.GetBlobClient(name).UploadAsync(content, overwrite: false, ct);
    }
}
```

- [ ] **Step 7: `TranscriptServiceCollectionExtensions.cs`** and wire-up

```csharp
using Azure.Core;
using Azure.Storage.Blobs;

namespace VoiceReset.Transcripts;

public static class TranscriptServiceCollectionExtensions
{
    public const string ContainerName = "transcripts";

    /// <summary>Blob writer when Storage:BlobEndpoint is set (managed identity via the shared TokenCredential), else the null writer.</summary>
    public static IServiceCollection AddTranscripts(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["Storage:BlobEndpoint"];
        if (string.IsNullOrEmpty(endpoint))
        {
            return services.AddSingleton<ITranscriptWriter, NullTranscriptWriter>();
        }
        return services.AddSingleton<ITranscriptWriter>(sp => new BlobTranscriptWriter(
            new BlobServiceClient(new Uri(endpoint), sp.GetRequiredService<TokenCredential>()).GetBlobContainerClient(ContainerName)));
    }
}
```

In `Program.cs` add `using VoiceReset.Transcripts;` and `builder.Services.AddTranscripts(builder.Configuration);` (after `AddJsonStore`, which registers the shared `TokenCredential`).

- [ ] **Step 8: Full check** — `dotnet build VoiceReset.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`; `dotnet test --project tests/VoiceReset.Tests` → all pass.

- [ ] **Step 9: Commit**

```bash
git add solution/src/VoiceReset solution/tests/VoiceReset.Tests/Transcripts
git commit -m "feat: add transcript recorder and blob writer"
```

---

## Self-review

- Masker: digit runs of 3+ as digits or words (incl. "oh", "double"/"triple") → `[CODE]`; rest of the turn after "password is" → `[REDACTED]`; URLs → `[LINK]`; normal text and "I have 2 laptops" unchanged. Recorder masks on add, keeps order, offsets from the fake clock. Writers and DI as in the contracts; blob name and create-only upload as specified.
- No logging here at all, so transcript text cannot reach logs from this code.

## Questions

1. "password is" redacts the rest of the **turn**, not just the sentence (safer; a password can contain `.` or `!`). OK? Other phrasings ("my password's …", "password: …") are not caught; spoken digits inside them still are if they form a run of 3+ separate words or digits.
2. Years and phone-like numbers ("2026", "five five five") are masked too. Acceptable for a debug aid?
3. Who creates the `transcripts` container: the setup script (T15) or a `CreateIfNotExistsAsync` at startup? This plan assumes T15.
4. When should T11 save: once at call end (`ToDocument` + `SaveAsync`, errors caught and logged by type)? A crash mid-call then loses that transcript. Fine for a debug aid?
5. Retention: blob lifecycle rule (e.g. delete after 7 days) in T15?

## Additions to contracts

- `TranscriptRecorder(string sessionId, string channel, TimeProvider time)` (adds `channel` for the document).
- `TranscriptTurn(string Role, long OffsetMs, string Text)`; `TranscriptDocument(string SessionId, string Channel, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string EndReason, IReadOnlyList<TranscriptTurn> Turns)`.
- `TranscriptServiceCollectionExtensions.AddTranscripts(IServiceCollection, IConfiguration)`, `ContainerName = "transcripts"`.
- `BlobTranscriptWriter.SaveAsync` throws `RequestFailedException` (409) if the blob exists; the caller (T11) catches and logs.
