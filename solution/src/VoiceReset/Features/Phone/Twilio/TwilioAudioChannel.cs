using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using VoiceReset.Features.Voice;

namespace VoiceReset.Features.Phone.Twilio;

/// <summary>
/// The phone, over a Twilio media stream (one WebSocket per call). Every message is JSON text: Twilio sends
/// connected, start, media (base64 μ-law 8 kHz), mark and stop; we send media, clear (barge-in) and mark.
/// Only one send runs at a time (a WebSocket allows one sender).
/// </summary>
public sealed class TwilioAudioChannel : IAudioChannel, IDisposable
{
    /// <summary>Twilio's media messages are small (20 ms of audio); anything this big is not Twilio.</summary>
    public const int MaxMessageBytes = 64 * 1024;

    private const string EndedMark = "ended";
    private static readonly TimeSpan s_startTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_playoutTimeout = TimeSpan.FromSeconds(8);

    private readonly WebSocket _socket;
    private readonly string _streamSid;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _buffer = new byte[MaxMessageBytes];
    // Set when Twilio has played everything sent before the "ended" mark, or the caller hung up.
    private readonly TaskCompletionSource _playedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TwilioAudioChannel(WebSocket socket, string streamSid)
    {
        _socket = socket;
        _streamSid = streamSid;
    }

    public string Name => "phone";

    public ChannelAudio Audio => ChannelAudio.MuLawAt8kHz;

    /// <summary>
    /// Reads up to the start message. Returns null unless it carries a token that redeem accepts
    /// (the one-time token from the signed webhook), so only calls Twilio announced get a voice session.
    /// </summary>
    public static async Task<TwilioAudioChannel?> AcceptAsync(WebSocket socket, Func<string?, bool> redeem, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(s_startTimeout);
        var buffer = new byte[MaxMessageBytes];
        while (await ReceiveAsync(socket, buffer, timeout.Token) is { } message)
        {
            using (message)
            {
                var root = message.RootElement;
                if (Text(root, "event") != "start")
                {
                    continue;   // "connected" comes first
                }
                var start = root.GetProperty("start");
                var token = start.TryGetProperty("customParameters", out var parameters) ? Text(parameters, "token") : null;
                return redeem(token) && Text(root, "streamSid") is { Length: > 0 } streamSid
                    ? new TwilioAudioChannel(socket, streamSid)
                    : null;
            }
        }
        return null;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await ReceiveAsync(_socket, _buffer, ct) is { } message)
        {
            using (message)
            {
                var root = message.RootElement;
                switch (Text(root, "event"))
                {
                    case "media":
                        yield return Convert.FromBase64String(Text(root.GetProperty("media"), "payload") ?? "");
                        break;
                    case "mark" when Text(root.GetProperty("mark"), "name") == EndedMark:
                        _playedOut.TrySetResult();
                        break;
                    case "stop":
                        _playedOut.TrySetResult();
                        yield break;   // the caller hung up
                }
            }
        }
        _playedOut.TrySetResult();
    }

    public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken ct) =>
        SendJsonAsync(new { @event = "media", streamSid = _streamSid, media = new { payload = Convert.ToBase64String(audio.Span) } }, ct);

    public Task StopPlaybackAsync(CancellationToken ct) => SendJsonAsync(new { @event = "clear", streamSid = _streamSid }, ct);

    public Task SendCaptionAsync(string speaker, string text, CancellationToken ct) => Task.CompletedTask;   // no screen

    /// <summary>A mark after the last audio: Twilio echoes it once the goodbye has been played to the caller.</summary>
    public Task SendEndedAsync(string reason, CancellationToken ct) =>
        SendJsonAsync(new { @event = "mark", streamSid = _streamSid, mark = new { name = EndedMark } }, ct);

    /// <summary>Waits until the goodbye was heard (at most a few seconds), then closes: Twilio then hangs up.</summary>
    public async Task CloseAsync(CancellationToken ct)
    {
        try
        {
            await _playedOut.Task.WaitAsync(s_playoutTimeout, ct);
        }
        catch (TimeoutException)
        {
            // close anyway
        }
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ended", ct);
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

    /// <summary>The next JSON message, or null when the socket closes.</summary>
    private static async Task<JsonDocument?> ReceiveAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        while (true)
        {
            var count = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (count == buffer.Length)
                {
                    throw new InvalidDataException("The phone stream sent a message larger than the limit.");
                }
                result = await socket.ReceiveAsync(buffer.AsMemory(count), ct);
                count += result.Count;
            }
            while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            if (result.MessageType == WebSocketMessageType.Text && count > 0)
            {
                return JsonDocument.Parse(buffer.AsMemory(0, count));
            }
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private async Task SendJsonAsync<T>(T message, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.SendAsync(data, WebSocketMessageType.Text, endOfMessage: true, ct);
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
