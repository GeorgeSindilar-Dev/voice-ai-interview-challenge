using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;

namespace VoiceReset.Features.Voice;

/// <summary>
/// The browser voice page over one WebSocket. Binary frames are PCM16 24 kHz mono in both
/// directions. Text frames from the server are JSON: {"type":"clear"}, {"type":"caption","speaker":"agent"|"caller","text":…}
/// and {"type":"ended","reason":…}. Text frames from the browser are ignored. Only one send runs
/// at a time (a WebSocket allows one sender).
/// </summary>
public sealed class BrowserAudioChannel(WebSocket socket) : IAudioChannel, IDisposable
{
    /// <summary>The page sends 100 ms frames (4,800 bytes); anything this big is not our page.</summary>
    public const int MaxFrameBytes = 64 * 1024;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public string Name => "browser";

    public ChannelAudio Audio => ChannelAudio.Pcm16At24kHz;

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[MaxFrameBytes];
        while (true)
        {
            var count = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (count == buffer.Length)
                {
                    throw new InvalidDataException("The browser sent a frame larger than the limit.");
                }
                result = await socket.ReceiveAsync(buffer.AsMemory(count), ct);
                count += result.Count;
            }
            while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                yield break;   // the caller ended the call (End button or tab closed)
            }
            if (result.MessageType == WebSocketMessageType.Binary && count > 0)
            {
                yield return buffer.AsMemory(0, count).ToArray();   // a copy: the buffer is reused
            }
        }
    }

    public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken ct) =>
        SendAsync(audio, WebSocketMessageType.Binary, ct);

    public Task StopPlaybackAsync(CancellationToken ct) => SendJsonAsync(new { type = "clear" }, ct);

    public Task SendCaptionAsync(string speaker, string text, CancellationToken ct) =>
        SendJsonAsync(new { type = "caption", speaker, text }, ct);

    public Task SendEndedAsync(string reason, CancellationToken ct) => SendJsonAsync(new { type = "ended", reason }, ct);

    public async Task CloseAsync(CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ended", ct);
            }
        }
        catch (WebSocketException)
        {
            // The caller is already gone: nothing to close.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Only the send lock; the WebSocket itself belongs to the endpoint.</summary>
    public void Dispose() => _sendLock.Dispose();

    private Task SendJsonAsync<T>(T message, CancellationToken ct) =>
        SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)), WebSocketMessageType.Text, ct);

    private async Task SendAsync(ReadOnlyMemory<byte> data, WebSocketMessageType type, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(data, type, endOfMessage: true, ct);
            }
        }
        catch (WebSocketException)
        {
            // The caller is gone. The read loop reports it as a dropped call; a send is not an error.
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
