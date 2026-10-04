using System.Text.Json.Nodes;
using Azure.AI.VoiceLive;

namespace VoiceReset.Features.Voice;

/// <summary>The real connection: a thin wrapper over the SDK's VoiceLiveSession (Azure.AI.VoiceLive 1.2.0).</summary>
public sealed class VoiceLiveConnection(VoiceLiveSession session) : IVoiceLiveConnection
{
    /// <summary>Opens the WebSocket. The model is fixed for the whole session.</summary>
    public static async Task<VoiceLiveConnection> OpenAsync(VoiceLiveClient client, string model, CancellationToken ct) =>
        new(await client.StartSessionAsync(model, ct));

    public Task ConfigureAsync(VoiceLiveSessionOptions options, CancellationToken ct) =>
        session.ConfigureSessionAsync(options, ct);

    // The SDK serializes its own sends, so audio may be sent while the event loop sends tool outputs.
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct) =>
        session.SendInputAudioAsync(BinaryData.FromBytes(pcm16), ct);

    public IAsyncEnumerable<SessionUpdate> ReadUpdatesAsync(CancellationToken ct) =>
        session.GetUpdatesAsync(ct);

    public Task SendFunctionOutputAsync(string callId, string outputJson, CancellationToken ct) =>
        session.AddItemAsync(new FunctionCallOutputItem(callId, outputJson), ct);

    public Task StartResponseAsync(string? instructions, CancellationToken ct) =>
        instructions is null ? session.StartResponseAsync(ct) : session.StartResponseAsync(instructions, ct);

    // The SDK's ResponseCreateParams (which has PreGeneratedAssistantMessage) is not public in 1.2.0,
    // so the command is sent as raw JSON. The message part has the SDK's own AssistantMessageItem shape.
    public Task SayAsync(string text, CancellationToken ct) =>
        session.SendCommandAsync(PreGeneratedResponse(text), ct);

    public ValueTask DisposeAsync() => session.DisposeAsync();

    /// <summary>{"type":"response.create","response":{"pre_generated_assistant_message":{...}}}</summary>
    public static BinaryData PreGeneratedResponse(string text)
    {
        var message = new JsonObject
        {
            ["type"] = "message",
            ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };
        var command = new JsonObject
        {
            ["type"] = "response.create",
            ["response"] = new JsonObject { ["pre_generated_assistant_message"] = message },
        };
        return BinaryData.FromString(command.ToJsonString());
    }
}
