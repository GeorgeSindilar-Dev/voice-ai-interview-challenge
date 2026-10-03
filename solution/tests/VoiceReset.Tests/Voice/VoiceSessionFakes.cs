using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Azure.AI.VoiceLive;
using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

/// <summary>Everything the fakes saw, in order; tests wait on it instead of sleeping.</summary>
public sealed class CallLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries];

    public Task Add(string entry)
    {
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public async Task WaitUntilAsync(Func<IReadOnlyList<string>, bool> condition)
    {
        for (var i = 0; i < 500 && !condition(Entries); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(Entries), "Timed out. Log: " + string.Join(" | ", Entries));
    }
}

/// <summary>The test plays the Voice Live service.</summary>
public sealed class FakeVoiceLiveConnection(CallLog log) : IVoiceLiveConnection
{
    private readonly Channel<SessionUpdate> _updates = Channel.CreateUnbounded<SessionUpdate>();

    public void Emit(SessionUpdate update) => _updates.Writer.TryWrite(update);

    /// <summary>Waits until every event emitted so far has been fully handled.</summary>
    public async Task SyncAsync()
    {
        var marker = $"sync-{Guid.NewGuid():N}";
        Emit(VoiceLiveModelFactory.SessionUpdateConversationItemTruncated(itemId: marker));
        await log.WaitUntilAsync(entries => entries.Contains($"read:{marker}"));
    }

    public async IAsyncEnumerable<SessionUpdate> ReadUpdatesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in _updates.Reader.ReadAllAsync(ct))
        {
            // Logged when the session asks for the next event, i.e. after it handled the previous one.
            await log.Add(update is SessionUpdateConversationItemTruncated sync ? $"read:{sync.ItemId}" : $"read:{update.GetType().Name}");
            yield return update;
        }
    }

    public Task ConfigureAsync(VoiceLiveSessionOptions options, CancellationToken ct) => log.Add("configure");
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct) => log.Add("audio-in");
    public Task SendFunctionOutputAsync(string callId, string outputJson, CancellationToken ct) => log.Add($"output:{callId}");
    public Task StartResponseAsync(string? instructions, CancellationToken ct) => log.Add($"response:{instructions}");
    public Task SayAsync(string text, CancellationToken ct) => log.Add($"say:{text}");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>The test plays the caller.</summary>
public sealed class FakeAudioChannel(CallLog log) : IAudioChannel
{
    private readonly Channel<ReadOnlyMemory<byte>> _incoming = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

    public string Name => "browser";
    public void HangUp() => _incoming.Writer.TryComplete();
    public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken ct) => _incoming.Reader.ReadAllAsync(ct);
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct) => log.Add("audio-out");
    public Task StopPlaybackAsync(CancellationToken ct) => log.Add("stop-playback");
    public Task SendCaptionAsync(string text, CancellationToken ct) => log.Add($"caption:{text}");
    public Task SendEndedAsync(string reason, CancellationToken ct) => log.Add($"ended:{reason}");
    public Task CloseAsync(CancellationToken ct) => log.Add("close");
}
