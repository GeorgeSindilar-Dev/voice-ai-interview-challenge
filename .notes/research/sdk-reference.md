# SDK reference: Voice Live, ACS Call Automation, browser audio, App Service

Research date: 2026-10-03. Audience: whoever writes the implementation plans. The code
in this document was checked against the real packages, not written from memory.

**How things were verified**

- **Compiled**: the C# snippets in sections A and B were compiled with the .NET 10 SDK
  (10.0.302) against `Azure.AI.VoiceLive` 1.2.0, `Azure.Communication.CallAutomation`
  1.6.1, `Azure.Identity` 1.21.0 and `Azure.Messaging.EventGrid` 5.0.0. The build
  finished with 0 errors and 0 warnings.
- **Reflected**: the public API of both SDKs was dumped by reflection from the NuGet
  assemblies. Every class, property and method name used below exists with that
  exact spelling and signature.
- **Serialized**: the session configuration, the function output item, ACS outbound
  messages and several inbound server events were serialized or deserialized with the
  SDK's own serializers, so the wire JSON shown below is real SDK output.
- **Docs and source**: facts come from Microsoft Learn pages (dated September 2026), the
  SDK source at tag `Azure.AI.VoiceLive_1.2.0`, and official samples. Each section links
  its sources.
- Anything not verified this way is marked **UNVERIFIED**.

---

## Read this first: ACS telephony is being retired (September 2026)

This changes the phone channel plan more than anything else in this document.

Microsoft announced in **September 2026** that Azure Communication Services is retiring
as a standalone offering ([retirement guide](https://learn.microsoft.com/en-us/azure/communication-services/acs-retirement-and-breaking-changes-guide),
updated 2026-09-24). The parts that affect us:

- "Beginning October 23, 2026, new customers can't sign up for Azure Communication
  Services retiring services." PSTN numbers (Direct Offer) and Direct Routing are
  **retired**. Call Automation and Audio Streaming get a **breaking change**: after
  September 30, 2028 they are supported only in Teams scenarios (Teams Phone
  Extensibility).
- Phone numbers: "Tenants that create their first ACS resource after the September 2026
  announcement aren't eligible to request phone numbers." and "customers who do not
  have existing ACS phone numbers will no longer be able to acquire new phone numbers."
- Trial numbers: for new ACS resources the portal still shows **Get phone number**, but
  "actions like acquiring or starting a trial/lookup will be unavailable (greyed out)".
  The guide adds: "If you have a critical business need, please contact support".
- The ACS concept pages now carry this banner: Call Automation, Call Recording and
  Audio Streaming "support only Microsoft Teams interoperability scenarios" once the
  Product Terms are updated.

**What this means for us:** unless the owner's tenant **already** has ACS phone numbers,
we can't get a number (trial or paid) for the optional phone channel. The browser voice
page becomes the only channel we can rely on. Section B still documents the ACS SDK
completely, in case a number turns up through an existing tenant or a support request.

---

## A. Voice Live (.NET SDK `Azure.AI.VoiceLive`)

### A.1 Package

| Item | Value | Source |
|---|---|---|
| NuGet ID | `Azure.AI.VoiceLive` | nuget.org flat container |
| Latest stable | **1.2.0** (2026-08-06) | `CHANGELOG.md` in the package |
| Latest preview | none newer than 1.2.0. The `1.2.0-beta.1` preview came before the stable release | nuget.org versions list |
| Target frameworks | `net10.0`, `net8.0`, `netstandard2.0` | `lib/` folders in the package |
| Dependency | `Azure.Core` 1.61.0 | nuspec |
| Default service API version | `2026-07-15` (GA): `VoiceLiveClientOptions.ServiceVersion.V2026_07_15` | changelog, reflection |
| Other service versions | `V2025_10_01`, `V2026_01_01_PREVIEW`, `V2026_04_10` | reflection |

Things to know about 1.2.0:

- `SmartEndOfTurnDetection`, the RTC call types, and the
  `output_audio_buffer.started/stopped` events were **removed** in the stable release.
  They existed only in the beta, so don't copy code from 1.2.0-beta.1 samples.
- The changelog lists `AzureRealtimeNativeVoice`, but reflection shows it is **not
  public** in 1.2.0. It is only needed for the `azure-realtime` model, which we don't
  plan to use.
- Most official samples still reference `1.1.0-beta.3` (for example
  [voicelive-samples/voice-live-universal-assistant/csharp](https://github.com/microsoft-foundry/voicelive-samples/tree/main/voice-live-universal-assistant/csharp)).
  The API names we use are the same in 1.2.0. The compile check above proves it.

### A.2 Resource, endpoint, regions, models, cost

**Resource.** Use a **Microsoft Foundry resource** (kind `AIServices`). An Azure Speech
resource also works, but the docs recommend Foundry resources for "full feature
availability" ([how-to](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-how-to)).
Voice Live models are fully managed: **we don't deploy a model**, we only pass its
name.

**Endpoint.** WebSocket URL:
`wss://<resource>.services.ai.azure.com/voice-live/realtime?api-version=2026-04-10&model=<model>`.
Older resources use `<resource>.cognitiveservices.azure.com`. With the SDK we pass only
the HTTPS base URI (`https://<resource>.services.ai.azure.com/`). The SDK source
(`VoiceLiveClient.WebSockets.cs`) switches the scheme to `wss`, appends
`/voice-live/realtime`, and adds `api-version` and `model` itself.

**Regions and models.** These come from the
[regions page, Voice Live tab](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/regions?tabs=voice-live)
(2026-09-30) and the [overview](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live)
(2026-09-29). Pricing tier is set by the model; you don't choose a tier.

| Tier | Models (selection) |
|---|---|
| **Pro** | `gpt-realtime`, `gpt-realtime-1.5`, `gpt-realtime-2.1` (+ `-datazone` variants), `gpt-4o`, `gpt-4.1`, `gpt-5`, `gpt-5.1`, `gpt-5.2`, `gpt-5.4`, `gpt-5.6-terra`, `azure-realtime` |
| **Standard** | `gpt-realtime-mini`, `gpt-realtime-2.1-mini`, `gpt-4o-mini`, `gpt-4.1-mini`, `gpt-5-mini`, `gpt-5.6-luna` |
| **Lite** | `gpt-4.1-nano`, `gpt-5-nano`, `phi4-mm-realtime` (preview) |

- **Multimodal** ("realtime") models hear the audio directly: `gpt-realtime*`,
  `gpt-realtime-mini`, `phi4-mm-realtime`.
- **Cascaded** models (`gpt-4o*`, `gpt-4.1*`, `gpt-5*`) use Azure speech to text for
  input and Azure text to speech for output.
- **HD voices** (for example `en-US-Ava:DragonHDLatestNeural`) work only in
  `southeastasia`, `centralindia`, `swedencentral`, `westeurope`, `eastus`, `eastus2`
  and `westus2` (how-to page).
- **Sweden Central** offers `gpt-4.1-mini` (Standard deployment), `gpt-realtime-mini`
  (Global standard) and HD voices, and it is in the EU. **East US 2** offers the same
  set.
- **West Europe** has **no** `gpt-realtime*` models. It has `gpt-4.1-mini` only as
  Data zone standard.

**Prices.** These are retail prices from the Azure Retail Prices API, `swedencentral`,
USD per 1M tokens, queried 2026-10-03:

| Meter | Pro | Standard | Lite |
|---|---|---|---|
| LLM text input / cached / output | 4.00 / 0.40 / 16.00 | 0.66 / 0.33 / 2.64 | 0.11 / 0.04 / 0.44 |
| LLM native audio input / output | 32.00 / 64.00 | 11.00 / 22.00 | 4.00 / 12.00 |
| Azure speech (standard) audio input / output | 17.00 / 31.00 | 15.00 / 26.00 | 15.00 / 25.00 |

Token rates from the overview page: about **10 tokens/s of input audio** and about
**20 tokens/s of output audio** for Azure OpenAI models.

**Rough cost of one minute of conversation** (60 s of input audio, 30 s of agent
speech, about 15K text tokens of prompt and history):

| Model | Estimate |
|---|---|
| `gpt-4.1-mini` | about $0.03/min |
| `gpt-realtime-mini` + Azure voice | about $0.03/min |
| `gpt-realtime` (Pro) | about $0.10/min |

These are estimates, not measurements.

**Recommended model: `gpt-4.1-mini`** (Standard tier) in **Sweden Central**, with
`azure-speech` transcription and an HD voice. Keep the model name in configuration so
`gpt-realtime-mini` can be tried with no code change.

Reasons:

1. It is a non-reasoning model, so time to first token is low. Its function calling and
   instruction following are mature.
2. As a cascaded model, the transcript the model reasons over is the same `azure-speech`
   transcript we store and guard. With a speech-to-speech model, the stored transcript
   comes from a separate transcription model and can differ from what the model "heard".
   That is weaker for the guardrail story.
3. `azure-speech` supports phrase lists, which helps with digit codes.

This quality judgment is **UNVERIFIED**: we ran no benchmark. Plan a short A/B test
against `gpt-realtime-mini` once the browser channel works.

### A.3 Authentication

**Recommended: Entra ID.**

```csharp
var client = new VoiceLiveClient(
    new Uri("https://<resource>.services.ai.azure.com/"),
    new DefaultAzureCredential());   // or ManagedIdentityCredential in Azure
```

- The token scope is hard-coded in the SDK as `https://ai.azure.com/.default`
  (`VoiceLiveClient.core.cs`). The docs also accept the legacy scope
  `https://cognitiveservices.azure.com/.default`. The SDK sends
  `Authorization: Bearer <token>` on the WebSocket handshake and gets tokens itself.
- **Role for the app's managed identity: `Cognitive Services User`** on the Foundry
  resource. The SDK README says so, and the official sample's Bicep assigns only this
  role for model mode.
- The how-to page now says to assign **both** `Cognitive Services User` and
  `Foundry User`. `Foundry User` was formerly named `Azure AI User`. Plan to assign both;
  that costs nothing and avoids a 401 on deployment day.
- **Key fallback:** `new VoiceLiveClient(endpoint, new AzureKeyCredential(key))`. The SDK
  sends the `api-key` header. Keys also work as an `api-key` query parameter, but
  never do that from a browser.

### A.4 Creating a session and configuring it (model mode)

`client.StartSessionAsync("gpt-4.1-mini")` opens and connects the WebSocket.
`session.ConfigureSessionAsync(options)` sends `session.update`. The service answers
with `session.created` and then `session.updated`.

The model is fixed for the whole session: "once a session is initialized with a
particular model, it can't be changed to another model" (API reference 2026-07-15).

```csharp
// Compiled against Azure.AI.VoiceLive 1.2.0
static VoiceLiveSessionOptions BuildSessionOptions(string instructions)
{
    var options = new VoiceLiveSessionOptions
    {
        Instructions = instructions,
        Voice = new AzureStandardVoice("en-US-Ava:DragonHDLatestNeural") { Temperature = 0.8f },
        InputAudioFormat = InputAudioFormat.Pcm16,       // 16-bit mono
        OutputAudioFormat = OutputAudioFormat.Pcm16,     // 24 kHz by default
        InputAudioSamplingRate = 24000,                  // 16000 or 24000 (default 24000)
        // Cascaded models (gpt-4.1*, gpt-5*): azure-speech. Realtime models: Gpt4oMiniTranscribe, Whisper1...
        InputAudioTranscription = new AudioInputTranscriptionOptions(AudioInputTranscriptionOptionsModel.AzureSpeech)
        {
            Language = "en-US",
        },
        TurnDetection = new AzureSemanticVadTurnDetection
        {
            Threshold = 0.5f,
            PrefixPadding = TimeSpan.FromMilliseconds(300),
            SilenceDuration = TimeSpan.FromMilliseconds(500),
            RemoveFillerWords = true,     // fewer false barge-ins on "uh", "mm"
            InterruptResponse = true,     // default true: user speech cancels the response
            AutoTruncate = true,          // default false: truncate the interrupted assistant item
            CreateResponse = true,        // default true: respond automatically at end of turn
        },
        InputAudioNoiseReduction = new AudioNoiseReduction(AudioNoiseReductionType.AzureDeepNoiseSuppression),
        InputAudioEchoCancellation = new AudioEchoCancellation(), // serializes as server_echo_cancellation
        Temperature = 0.7f,
        ToolChoice = new ToolChoiceOption(ToolChoiceLiteral.Auto),
        AllowParallelToolCalls = false,   // one tool call per response keeps the state machine simple
        MaxResponseOutputTokens = new MaxResponseOutputTokensOption(400),
    };
    options.Modalities.Clear();
    options.Modalities.Add(InteractionModality.Text);
    options.Modalities.Add(InteractionModality.Audio);

    options.Tools.Add(new VoiceLiveFunctionDefinition("submit_code")
    {
        Description = "Submit the verification code the caller read out.",
        Parameters = BinaryData.FromString("""
        {
          "type": "object",
          "properties": { "code": { "type": "string", "description": "Six digits as spoken." } },
          "required": ["code"],
          "additionalProperties": false
        }
        """),
    });
    return options;
}
```

This is the wire JSON the SDK produced for those options, abbreviated:

```json
{"model":"gpt-realtime-mini","modalities":["text","audio"],
 "voice":{"type":"azure-standard","name":"en-US-Ava:DragonHDLatestNeural","temperature":0.8},
 "instructions":"...","input_audio_sampling_rate":24000,
 "input_audio_format":"pcm16","output_audio_format":"pcm16",
 "input_audio_noise_reduction":{"type":"azure_deep_noise_suppression"},
 "input_audio_echo_cancellation":{"type":"server_echo_cancellation"},
 "input_audio_transcription":{"model":"azure-speech","language":"en-US"},
 "tools":[{"type":"function","name":"submit_code","description":"...","parameters":{...}}],
 "tool_choice":"auto","parallel_tool_calls":false,"temperature":0.7,
 "max_response_output_tokens":400,
 "turn_detection":{"type":"azure_semantic_vad","threshold":0.5,"prefix_padding_ms":300,
   "silence_duration_ms":500,"remove_filler_words":true,"auto_truncate":true,
   "create_response":true,"interrupt_response":true}}
```

Notes:

- **Transcription model depends on the chat model.**
  - `azure-speech` is for "all non-multimodal models".
  - `whisper-1`, `gpt-4o-transcribe`, `gpt-4o-mini-transcribe` and
    `gpt-4o-transcribe-diarize` work only with `gpt-realtime` and `gpt-realtime-mini`.
  - `mai-transcribe` works with both kinds.
  - The official C# sample auto-corrects a cascaded model to `azure-speech`. Make the
    transcription model a config value that matches the chat model.
- **Turn detection types.** Besides `AzureSemanticVadTurnDetection` the SDK has
  `AzureSemanticVadTurnDetectionEn`, `AzureSemanticVadTurnDetectionMultilingual`,
  `ServerVadTurnDetection` and `NoTurnDetection`.
  - `azure_semantic_vad` works with **all** models.
  - `interrupt_response` is only available on the Azure semantic types.
  - Defaults since `2026-04-10`: `prefix_padding_ms` 420 (semantic) or 400 (server VAD),
    `speech_duration_ms` 80 (semantic) or 200 (server VAD), `silence_duration_ms` 500,
    `threshold` 0.5.
- **End-of-utterance detection.** The SDK still exposes
  `AzureSemanticVadTurnDetection.EndOfUtteranceDetection = new AzureSemanticEouDetection
  { ThresholdLevel = EouThresholdLevel.Default, Timeout = TimeSpan.FromSeconds(2) }`,
  which serializes to `{"model":"semantic_detection_v1","threshold_level":"default","timeout_ms":2000}`.
  The current how-to page no longer documents it. Treat it as **UNVERIFIED** and leave it
  out of version 1.
- **Echo cancellation.** `server_echo_cancellation` uses the service's own output as the
  echo reference. It assumes playback starts within about 2 s of receipt.
  "Live-Reference AEC" (`ReferenceSource = Client`, `Channels = 2`) needs interleaved
  stereo input. Don't use it.
- **Voice.** `AzureStandardVoice(name)` supports `Temperature` (HD voices only),
  `Rate` ("0.5" to "1.5"), `Locale`, `Style`, `Pitch` and `Volume`.
- **Mid-session updates.** `ConfigureSessionAsync` can be called again mid-session, for
  example to change instructions per step, as the Node reference does. You can't change
  `input_audio_format`, the sampling rate or the echo-cancellation reference mid-session.
- **Response-level overrides.** `StartResponseAsync(string additionalInstructions)` adds
  per-response instructions. `AddItemAsync(new SystemMessageItem("..."))` injects a system
  message. That item serializes as `{"type":"message","role":"system","content":[{"type":"input_text","text":"..."}]}`.

### A.5 Sending audio

- `session.SendInputAudioAsync(byte[] pcm16, ct)` base64-encodes the audio and sends
  `input_audio_buffer.append`. This is SDK source behavior.
- The maximum chunk size is 15 MiB per event. We send about 20 ms (ACS) or 100 ms
  (browser) per call.
- With server turn detection, **never** call `CommitInputAudioAsync`; the VAD commits.
- Thread safety: `VoiceLiveSession` serializes sends with internal semaphores (one for
  audio, one for commands). Audio can be sent from the channel's receive loop while
  the event loop sends tool outputs.
- Don't use `SendInputAudioAsync(Stream)`. It owns the "audio stream" flag and makes
  the `byte[]` overload throw while it runs.
- Don't use `ClearStreamingAudioAsync`. It sends an event that isn't in the documented
  2026-07-15 event list.

### A.6 Receiving events

`await foreach (SessionUpdate update in session.GetUpdatesAsync(ct))` and use a C#
`switch` on the type. Only one receive loop per session is allowed: the SDK locks a
single receive collection.

| Wire event | SDK type | Useful members |
|---|---|---|
| `session.created` | `SessionUpdateSessionCreated` | `Session.Id`, `Session.ExpiresOn` |
| `session.updated` | `SessionUpdateSessionUpdated` | `Session` |
| `input_audio_buffer.speech_started` | `SessionUpdateInputAudioBufferSpeechStarted` | `AudioStart` (TimeSpan), `ItemId` |
| `input_audio_buffer.speech_stopped` | `SessionUpdateInputAudioBufferSpeechStopped` | `AudioEnd`, `ItemId` |
| `conversation.item.input_audio_transcription.completed` | `SessionUpdateConversationItemInputAudioTranscriptionCompleted` | `ItemId`, `Transcript` |
| `conversation.item.input_audio_transcription.failed` | `SessionUpdateConversationItemInputAudioTranscriptionFailed` | `Error` |
| `response.created` | `SessionUpdateResponseCreated` | `Response.Id` |
| `response.output_item.added` | `SessionUpdateResponseOutputItemAdded` | `Item` (`ResponseFunctionCallItem` / `SessionResponseMessageItem`) |
| `response.audio.delta` | `SessionUpdateResponseAudioDelta` | `Delta` (**already decoded** PCM bytes, checked by deserializing), `ItemId`, `ResponseId` |
| `response.audio.done` | `SessionUpdateResponseAudioDone` | |
| `response.audio_transcript.delta` / `.done` | `SessionUpdateResponseAudioTranscriptDelta` / `...Done` | `Delta` / `Transcript` |
| `response.function_call_arguments.done` | `SessionUpdateResponseFunctionCallArgumentsDone` | `Name`, `CallId`, `Arguments` (JSON string), `ItemId` |
| `response.done` | `SessionUpdateResponseDone` | `Response.Status` (`SessionResponseStatus.Completed/Cancelled/Failed/Incomplete`), `Response.StatusDetails`, `Response.Usage` |
| `conversation.item.truncated` | `SessionUpdateConversationItemTruncated` | `ItemId`, `AudioEnd` |
| `error` | `SessionUpdateError` | `Error.Code`, `Error.Message`, `Error.Type`, `Error.EventId` |
| `warning` | `ServerEventWarning` | `Warning.Code`, `Warning.Message` |

A barge-in cancellation arrives as `response.done` with `Status == cancelled`. Its
`StatusDetails` is a `ResponseCancelledDetails` with
`Reason == ResponseCancelledDetailsReason.TurnDetected`. A client cancel gives
`ClientCancelled`. This was checked by deserializing a sample event.

### A.7 Function calls

1. On `SessionUpdateResponseFunctionCallArgumentsDone`, parse `Arguments`, run the tool
   in **our** backend, then call
   `await session.AddItemAsync(new FunctionCallOutputItem(call.CallId, jsonResult), ct)`.
   The item serializes as `{"type":"function_call_output","call_id":"...","output":"..."}`.
2. Then request the next response with `StartResponseAsync()`, but **only when no
   response is active**. The response that contained the function call is still in
   progress until its `response.done`. A second `response.create` while one is active
   fails with `conversation_already_has_active_response`. The Node reference handles
   this with an "active plus pending" flag; the C# loop below does the same.
3. Handle function calls **only** in `...FunctionCallArgumentsDone`. The official
   `CustomerServiceBot` sample also handles `ResponseOutputItemAdded`. That risks running
   a tool twice if arguments are ever present there.

### A.8 Barge-in, cancelling and truncating

- With `azure_semantic_vad` and `InterruptResponse = true` (the default), **the service
  cancels the in-progress response itself** when the user starts speaking. Our job is to
  **flush client playback** on `SessionUpdateInputAudioBufferSpeechStarted`: send ACS
  `StopAudio`, or send a `clear` control message to the browser.
- `AutoTruncate = true` lets the service truncate the interrupted assistant item, so the
  conversation history matches what was heard. It assumes real-time playback.
- Manual controls, if needed:
  - `session.CancelResponseAsync()` sends `response.cancel`.
  - `session.TruncateConversationAsync(itemId, contentIndex: 0, audioEnd: TimeSpan, ct)`
    sends `conversation.item.truncate` with `audio_end_ms`.
- Sending `response.cancel` when nothing is active returns an `error` event (OpenAI
  Realtime behavior; **UNVERIFIED** for Voice Live). Only cancel while our
  `responseActive` flag is set.

### A.9 Complete minimal example: connect, configure, audio, events, tools, barge-in

The `RunVoiceLiveEventLoop` method and `BuildSessionOptions` compiled cleanly. The top
three statements are glue: `ct`, `SendPcmToChannel` and `FlushChannelPlayback` are
placeholders for the channel code. The channel is abstracted as two delegates
(`playAudio`, `stopPlayback`), so the same loop serves the browser and ACS. That matches
the CLAUDE.md rule that channels only move audio.

```csharp
using System.Text.Json;
using Azure.AI.VoiceLive;
using Azure.Identity;

var client = new VoiceLiveClient(new Uri("https://<resource>.services.ai.azure.com/"), new DefaultAzureCredential());

await using VoiceLiveSession session = await client.StartSessionAsync("gpt-4.1-mini", ct);
await session.ConfigureSessionAsync(BuildSessionOptions("You help callers reset a password..."), ct);

// Channel receive loop elsewhere: for each 20-100 ms PCM16 24 kHz mono frame:
//     await session.SendInputAudioAsync(frame, ct);

await RunVoiceLiveEventLoop(session, playAudio: SendPcmToChannel, stopPlayback: FlushChannelPlayback, ct);

static async Task RunVoiceLiveEventLoop(
    VoiceLiveSession session,
    Func<byte[], Task> playAudio,
    Func<Task> stopPlayback,
    CancellationToken ct)
{
    bool responseActive = false;   // only touched on this loop's thread of control
    bool responsePending = false;
    bool greeted = false;

    async Task RequestResponseAsync()
    {
        if (responseActive) { responsePending = true; return; }
        responseActive = true;
        await session.StartResponseAsync(ct);
    }

    await foreach (SessionUpdate update in session.GetUpdatesAsync(ct))
    {
        switch (update)
        {
            case SessionUpdateSessionUpdated:
                // session.updated also follows every later ConfigureSessionAsync call: greet only once
                if (!greeted) { greeted = true; await RequestResponseAsync(); }
                break;

            case SessionUpdateInputAudioBufferSpeechStarted:
                await stopPlayback();                         // barge-in: flush channel; service cancels the response
                break;

            case SessionUpdateResponseCreated:
                responseActive = true;
                break;

            case SessionUpdateResponseAudioDelta delta:
                await playAudio(delta.Delta.ToArray());       // raw PCM16 24 kHz mono
                break;

            case SessionUpdateConversationItemInputAudioTranscriptionCompleted user:
                // user.Transcript -> mask, then append to transcript
                break;

            case SessionUpdateResponseAudioTranscriptDone agent:
                // agent.Transcript -> mask, then append to transcript
                break;

            case SessionUpdateResponseFunctionCallArgumentsDone call:
                // Backend decides. Never trust IDs from the model.
                string result = JsonSerializer.Serialize(new { ok = true });
                await session.AddItemAsync(new FunctionCallOutputItem(call.CallId, result), ct);
                await RequestResponseAsync();                 // deferred until response.done if needed
                break;

            case SessionUpdateResponseDone done:
                responseActive = false;
                // done.Response.Status / StatusDetails (ResponseCancelledDetails on barge-in) / Usage
                if (responsePending) { responsePending = false; await RequestResponseAsync(); }
                break;

            case SessionUpdateError error:
                // error.Error.Code / Message: log the code only, never the content
                break;
        }
    }
}
```

The tool runs inline in the event loop, so audio deltas queue while it runs. That's
fine for fast tools (milliseconds to about 1 s). If a tool can be slow, add an "interim
response": the SDK has `StaticInterimResponseConfig` and `LlmInterimResponseConfig`. Or
run the tool on a separate task and post the result back.

### A.10 Limits

From [Speech quotas](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits)
(2026-09-09), per resource, Standard (S0):

- New connections per minute: **100**. The quota-increase section of the same page says
  "TPM = NCPM × 4,000", for example 30 NCPM gives 120,000 TPM. The two numbers don't
  agree; **UNVERIFIED** which one applies.
- **Maximum connection length: 60 minutes per session.** `session.created` carries
  `ExpiresOn`.
- **Tokens per minute: 120,000.** Raising the connection quota also raises TPM.
- No explicit concurrent-session limit is documented. At about 1-2K tokens per
  conversation-minute, 120K TPM supports dozens of simultaneous calls.

### A.11 Security notes for our rules

- SDK content logging is off unless `ClientOptions.Diagnostics.IsLoggingContentEnabled`
  is true (`VoiceLiveWebSocketContentLogger.cs`). Keep the default.
- SDK OpenTelemetry content capture (transcripts, function arguments) is off unless
  `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT` or
  `AZURE_TRACING_GEN_AI_CONTENT_RECORDING_ENABLED` is `true`. Make sure neither is set in
  App Service, because function arguments contain verification codes.

### A.12 Links

- SDK source at tag: https://github.com/Azure/azure-sdk-for-net/tree/Azure.AI.VoiceLive_1.2.0/sdk/voicelive/Azure.AI.VoiceLive
- Samples: https://github.com/Azure/azure-sdk-for-net/tree/main/samples/voicelive (see `customer-service-bot`)
- Full-stack official sample (C# backend plus browser worklets): https://github.com/microsoft-foundry/voicelive-samples/tree/main/voice-live-universal-assistant
- How-to: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-how-to
- API reference 2026-07-15: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/voice-live-api-reference-2026-07-15
- Node reference implementation (behavior model): the pinned "IT help desk password-reset gallery demo" linked in the challenge [README](../../README.md) (folder `docs/templates/it-helpdesk-password-reset/code` at commit `ea32df55`)

### A.13 UNVERIFIED / risks

- Whether `Foundry User` is really required in addition to `Cognitive Services User`.
  The docs say both; the official sample uses only the latter.
- Model quality ranking (`gpt-4.1-mini` versus `gpt-realtime-mini`) for our tool-heavy
  flow. Not benchmarked.
- Whether streamed silence is billed as input audio tokens. The cost estimate assumes
  all streamed audio is billed.
- Current status of `end_of_utterance_detection`. It is still in the SDK but not in the
  current docs.
- The connections-per-minute default: 100 or 30.
- Error behavior of `response.cancel` with no active response.

---

## B. ACS Call Automation (.NET `Azure.Communication.CallAutomation`)

> Read the retirement section at the top first. Everything below is accurate for the
> SDK, but getting a phone number is probably blocked for our tenant.

### B.1 Package and client

- NuGet `Azure.Communication.CallAutomation` **1.6.1** (2026-09-29), stable. Targets
  `net10.0`, `net8.0` and `netstandard2.0`. Service versions run up to `V2026_03_12`.
- Bidirectional streaming, `AudioFormat.Pcm24KMono`, `OutStreamingData` and
  `StreamingData.Parse` have been GA since 1.4.0.

Constructors, verified by reflection and the compile check:

```csharp
var acs = new CallAutomationClient("<connection string>");                       // key-based
var acs = new CallAutomationClient(new Uri("https://<name>.communication.azure.com/"),
                                   new DefaultAzureCredential());              // Entra / managed identity
// The full signature is (Uri, TokenCredential, CallAutomationClientOptions options = null)
```

Entra auth: the ACS auth page lists "Call Automation: Access Key or Microsoft Entra
authentication". The role is **UNVERIFIED**. The ACS quickstart uses **`Contributor`**.
We found no documented least-privilege data role for Call Automation. Assign
`Contributor` scoped to the ACS resource only, or fall back to a Key Vault-stored
connection string.

### B.2 Inbound call flow

1. **Event Grid subscription.** Create it on the ACS resource's system topic, with event
   type `Microsoft.Communication.IncomingCall`, a Webhook endpoint, and **Event Grid
   schema**. Recommended settings (incoming call concept page):
   - Max delivery attempts **2** and event TTL **1 minute**, because a call rings for
     only **30 s**.
   - An advanced filter on `data.to.PhoneNumber.Value`.
   - Handle duplicates, because delivery is at least once.
   - Host the endpoint on always-on compute.
2. **Validation handshake.** Event Grid first posts a `SubscriptionValidationEvent`.
   Answer with `SubscriptionValidationResponse { ValidationResponse = code }`.
3. **Answer the call.** Call `AnswerCallAsync` with `MediaStreamingOptions`. Media
   streaming starts automatically, and ACS opens a WebSocket to our `TransportUri`.

```csharp
// Compiled: Azure.Communication.CallAutomation 1.6.1 + Azure.Messaging.EventGrid 5.0.0
app.MapPost("/api/acs/incoming-call", async (HttpRequest request, CallAutomationClient acs, IConfiguration config) =>
{
    BinaryData body = await BinaryData.FromStreamAsync(request.Body);
    foreach (EventGridEvent egEvent in EventGridEvent.ParseMany(body))
    {
        if (!egEvent.TryGetSystemEventData(out object systemEvent)) continue;
        switch (systemEvent)
        {
            case SubscriptionValidationEventData validation:
                return Results.Ok(new SubscriptionValidationResponse { ValidationResponse = validation.ValidationCode });

            case AcsIncomingCallEventData incoming:
                string publicHost = config["PublicHost"]!;
                string callId = Guid.NewGuid().ToString("N");            // our id, unguessable
                var options = new AnswerCallOptions(incoming.IncomingCallContext,
                                                    new Uri($"https://{publicHost}/api/acs/callbacks/{callId}"))
                {
                    MediaStreamingOptions = new MediaStreamingOptions(MediaStreamingAudioChannel.Mixed)
                    {
                        TransportUri = new Uri($"wss://{publicHost}/ws/acs-media/{callId}"),
                        MediaStreamingContent = MediaStreamingContent.Audio,
                        StartMediaStreaming = true,
                        EnableBidirectional = true,
                        AudioFormat = AudioFormat.Pcm24KMono,
                        // EnableDtmfTones = true,   // optional: DTMF arrives as DtmfData on the media socket
                    },
                    OperationContext = callId,
                };
                AnswerCallResult answered = await acs.AnswerCallAsync(options);
                // answered.CallConnection.CallConnectionId -> store with callId
                // incoming.FromCommunicationIdentifier?.RawId is the caller (PII: don't log raw)
                break;
        }
    }
    return Results.Ok();
});
```

Notes on these names in 1.6.1:

- `MediaStreamingOptions` has **one** public constructor:
  `(MediaStreamingAudioChannel audioChannelType, StreamingTransport streamingTransport = websocket)`.
  The transport defaults to `websocket` (checked at runtime).
- The settable properties are `TransportUri`, `MediaStreamingContent`,
  `StartMediaStreaming`, `EnableBidirectional`, `AudioFormat` and `EnableDtmfTones`.
- The audio channel enum is `MediaStreamingAudioChannel.Mixed` / `.Unmixed`. Use
  `Mixed`: the agent's own playback isn't in the inbound mixed stream (UNVERIFIED), and
  there is one caller.
- `AudioFormat` has only `Pcm16KMono` and `Pcm24KMono`. Choosing `Pcm24KMono` means no
  resampling to or from Voice Live.

### B.3 Callback events

Callbacks are CloudEvents posted to the `CallbackUri`. Parse them with
`CallAutomationEventParser.Parse(CloudEvent)` or `CallAutomationEventParser.ParseMany(BinaryData)`.

```csharp
app.MapPost("/api/acs/callbacks/{callId}", async (string callId, HttpRequest request, CallAutomationClient acs) =>
{
    BinaryData body = await BinaryData.FromStreamAsync(request.Body);
    foreach (CloudEvent ce in CloudEvent.ParseMany(body))
    {
        // ce.Id is the dedupe key (at-least-once delivery)
        switch (CallAutomationEventParser.Parse(ce))
        {
            case CallConnected c:         /* c.CallConnectionId, c.CorrelationId */ break;
            case MediaStreamingStarted s: /* s.MediaStreamingUpdate.MediaStreamingStatus */ break;
            case MediaStreamingFailed f:  await acs.GetCallConnection(f.CallConnectionId).HangUpAsync(forEveryone: true); break;
            case MediaStreamingStopped:   break;
            case CallDisconnected d:      /* end the Voice Live session, save transcript */ break;
        }
    }
    return Results.Ok();
});
```

- Every event derives from `CallAutomationEventBase` (`CallConnectionId`,
  `ServerCallId`, `CorrelationId`, `OperationContext`, `ResultInformation`).
- Other relevant types: `AnswerFailed`, `PlayCompleted`,
  `ContinuousDtmfRecognitionToneReceived`.
- `MediaStreamingUpdate.MediaStreamingStatusDetails` includes
  `InitialWebSocketConnectionFailed`, `StreamConnectionInterrupted` and others.

### B.4 Media WebSocket protocol

The ACS docs give these numbers: 50 frames/s, 20 ms packets, 960 bytes per packet at
24 kHz (640 at 16 kHz), 16-bit PCM mono, base64 payload.

Inbound messages (text frames, camelCase) and the SDK parser:

```json
{"kind":"AudioMetadata","audioMetadata":{"subscriptionId":"...","encoding":"PCM","sampleRate":24000,"channels":1,"length":640}}
{"kind":"AudioData","audioData":{"timestamp":"2024-05-01T10:00:00.000Z","participantRawID":"4:+1555...","data":"<base64 pcm>","silent":false}}
{"kind":"DtmfData","dtmfData":{"data":"5"}}
```

```csharp
switch (StreamingData.Parse(json))               // returns AudioMetadata / AudioData / DtmfData / ...
{
    case AudioMetadata m: /* m.Encoding, m.SampleRate, m.Channels, m.MediaSubscriptionId */ break;
    case AudioData a when !a.IsSilent: await session.SendInputAudioAsync(a.Data.ToArray(), ct); break;
    case DtmfData d: /* d.Data */ break;
}
```

All three parse correctly with the SDK (runtime check). `AudioData` properties are
`Data` (`ReadOnlyMemory<byte>`, already decoded), `Timestamp`, `Participant` and
`IsSilent`.

Outbound messages are built with the SDK helpers. This is their exact output:

```csharp
string play = OutStreamingData.GetAudioDataForOutbound(pcmBytes);
// {"Kind":"AudioData","AudioData":{"Data":"<base64>","Timestamp":"0001-01-01T00:00:00+00:00","Participant":null,"IsSilent":false},"StopAudio":null}
string stop = OutStreamingData.GetStopAudioForOutbound();
// {"Kind":"StopAudio","AudioData":null,"StopAudio":{}}
```

The Node reference sends camelCase (`{kind:"AudioData", audioData:{data}}` and
`{kind:"StopAudio", stopAudio:{}}`), so the service evidently accepts both casings.

Receive loop rules:

- Loop until `EndOfMessage`. The official C# sample uses a fixed 2048-byte buffer with
  no `EndOfMessage` loop, which is fragile.
- Serialize sends to the ACS socket with a `SemaphoreSlim`. `WebSocket.SendAsync` must
  not run concurrently.

### B.5 Securing callbacks and the media WebSocket

From [Secure webhook endpoint](https://learn.microsoft.com/en-us/azure/communication-services/how-tos/call-automation/secure-webhook-endpoint):

- **Callbacks.** Every mid-call callback carries a signed JWT (`Authorization: Bearer`).
  The token lifetime is 5 minutes.
  - OpenID config: `https://acscallautomation.communication.azure.com/calling/.well-known/acsopenidconfiguration`
  - JWKS: `https://acscallautomation.communication.azure.com/calling/keys`
  - Issuer: `https://acscallautomation.communication.azure.com`
  - Audience: the ACS **immutable resource ID**, a GUID shown in the resource
    properties.
  - Validate with `Microsoft.AspNetCore.Authentication.JwtBearer`, using
    `ConfigurationManager<OpenIdConnectConfiguration>` and
    `TokenValidationParameters.ValidAudience`.
  - Alternative: a secret token in the callback query string. Our generated unguessable
    `callId` path segment partly covers this.
- **Media WebSocket.** "Each WebSocket connection request made by Call Automation now
  includes a signed JWT in the authentication header". Lifetime 24 hours; validate it
  the same way.
  - In ASP.NET Core, call `await context.AuthenticateAsync()` before
    `AcceptWebSocketAsync()`.
  - The headers `x-ms-call-connection-id` and `x-ms-call-correlation-id` are also sent.
    Check that the connection ID matches the call we answered.
- **IncomingCall (Event Grid)** isn't signed by ACS. Use the Event Grid validation
  handshake plus Entra-protected webhook delivery, or a secret query parameter on the
  subscription URL.

### B.6 Hang up, call duration, DTMF

- Hang up with
  `await acs.GetCallConnection(callConnectionId).HangUpAsync(forEveryone: true)`.
  Treat failures as "already gone", as the reference does.
- **Max call duration:** we found no documented limit for paid numbers (**UNVERIFIED**).
  Trial numbers cap calls at **5 minutes**. Enforce our own limit in the backend.
- **DTMF while streaming:** there are two options in 1.6.1.
  1. Set `MediaStreamingOptions.EnableDtmfTones = true`. Tones then arrive on the media
     socket as `DtmfData`.
  2. Use `CallMedia.StartContinuousDtmfRecognitionAsync(...)`, which delivers
     `ContinuousDtmfRecognitionToneReceived` callbacks.

  Neither was exercised end-to-end, and coexistence with bidirectional streaming is
  **UNVERIFIED**.

### B.7 Trial numbers and toll-free eligibility (largely moot, see retirement)

- **Trial number**
  ([FAQ](https://learn.microsoft.com/en-us/azure/communication-services/concepts/telephony/trial-phone-numbers-faq)):
  - One per customer, **US subscriptions only**. "If your Azure billing address is
    outside of the United States, you are not eligible to acquire trial phone numbers."
  - It is a US toll-free number valid for **30 days**, with **60 min inbound and 60 min
    outbound** calling and a **5-min call cap**.
  - Recipient verification (up to 3 US +1 numbers) is described for calls the trial
    number makes. Whether **inbound calls from unverified callers** are accepted is
    **UNVERIFIED**.
  - For new ACS resources after the September 2026 announcement, trial activation is
    greyed out.
- **Purchased US toll-free / local numbers**
  ([US page](https://learn.microsoft.com/en-us/azure/communication-services/concepts/numbers/phone-number-management-for-united-states)):
  - You need a paid subscription; "You can't acquire phone numbers using Azure free
    credits".
  - Eligible agreements: MCA, CSP, EA and Pay-As-You-Go.
  - Allowed **billing locations**: Australia, Canada, Denmark, France, Germany, Ireland,
    Italy, Japan, Netherlands, Puerto Rico, Spain, Sweden, Switzerland, United Kingdom,
    United States. A billing country outside this list can't buy US numbers.
  - For new tenants this is now overridden by the retirement: no numbers at all.

### B.8 Official samples

- **ACS plus Voice Live, C#:**
  [Azure-Samples/communication-services-dotnet-quickstarts/CallAutomation_AzureAI_VoiceLive](https://github.com/Azure-Samples/communication-services-dotnet-quickstarts/tree/main/CallAutomation_AzureAI_VoiceLive).
  - It uses CallAutomation 1.4.0, a connection string, and a raw `ClientWebSocket` to
    `/voice-agent/realtime?api-version=2025-05-01-preview` with the **API key in the
    query string** (don't copy that).
  - Key ideas worth copying: answer with `MediaStreamingOptions(MediaStreamingAudioChannel.Mixed)`
    plus `EnableBidirectional`/`Pcm24KMono`; forward `AudioData` when `!IsSilent`;
    on `response.audio.delta` send `OutStreamingData.GetAudioDataForOutbound(...)`; on
    `input_audio_buffer.speech_started` send `OutStreamingData.GetStopAudioForOutbound()`.
- **ACS plus Azure OpenAI realtime, C#:**
  [CallAutomation_AzOpenAI_Voice](https://github.com/Azure-Samples/communication-services-dotnet-quickstarts/tree/main/CallAutomation_AzOpenAI_Voice)
  and [callautomation-openai-sample-csharp](https://github.com/Azure-Samples/communication-services-dotnet-quickstarts/tree/main/callautomation-openai-sample-csharp).
  We only found these folders; we didn't review them.

### B.9 UNVERIFIED / risks

- **Highest risk:** we can't get any ACS phone number on a new tenant (retirement).
  Check whether the owner's tenant already has ACS numbers before spending any time on
  the phone channel.
- The least-privilege RBAC role for Call Automation with managed identity. `Contributor`
  works per the quickstart pattern.
- The maximum call duration for paid numbers.
- DTMF together with bidirectional streaming.
- Whether the mixed inbound stream excludes our own injected audio.
- Inbound calls to a trial number from unverified callers.

---

## C. Browser side (vanilla JS, AudioWorklet)

### C.1 What the official sample does

The Microsoft full-stack sample
([voice-live-universal-assistant/frontend](https://github.com/microsoft-foundry/voicelive-samples/tree/main/voice-live-universal-assistant/frontend))
works like this:

- It creates `new AudioContext({ sampleRate: 24000 })` for capture and for playback, and
  lets the browser resample the 48 kHz microphone.
- A capture worklet converts Float32 to Int16.
- A playback worklet keeps a queue of Int16 buffers. Posting `null` **clears the queue**;
  that is the barge-in flush.
- It sends 100 ms chunks (4,800 bytes) as base64 JSON.

We copy that design with two simplifications:

1. One `AudioContext` for both directions, so a single user gesture starts everything.
2. **Binary** WebSocket frames for audio. JSON text frames carry control messages only.

### C.2 Minimal sketch

The code below follows our "clean console" rule: no `console.*`, and errors go to the
UI.

`/js/pcm-worklets.js` (served as a static file, same origin):

```js
// Capture: Float32 [-1,1] at the context rate (24 kHz) -> Int16 chunks of 100 ms.
class CaptureProcessor extends AudioWorkletProcessor {
  constructor() { super(); this.buf = new Int16Array(2400); this.n = 0; }
  process(inputs) {
    const ch = inputs[0] && inputs[0][0];
    if (ch) {
      for (let i = 0; i < ch.length; i++) {
        const s = Math.max(-1, Math.min(1, ch[i]));
        this.buf[this.n++] = s < 0 ? s * 0x8000 : s * 0x7fff;
        if (this.n === this.buf.length) {
          this.port.postMessage(this.buf.buffer, [this.buf.buffer]);
          this.buf = new Int16Array(2400); this.n = 0;
        }
      }
    }
    return true;
  }
}
registerProcessor('capture', CaptureProcessor);

// Playback: queue of Int16 PCM at 24 kHz; message null = flush (barge-in).
class PlaybackProcessor extends AudioWorkletProcessor {
  constructor() {
    super(); this.queue = []; this.cur = null; this.pos = 0;
    this.port.onmessage = (e) => {
      if (e.data === null) { this.queue = []; this.cur = null; this.pos = 0; }
      else this.queue.push(new Int16Array(e.data));
    };
  }
  process(_, outputs) {
    const out = outputs[0][0];
    for (let i = 0; i < out.length; i++) {
      if (!this.cur || this.pos >= this.cur.length) {
        this.cur = this.queue.shift() || null; this.pos = 0;
        if (!this.cur) { out[i] = 0; continue; }
      }
      out[i] = this.cur[this.pos++] / 32768;
    }
    return true;
  }
}
registerProcessor('playback', PlaybackProcessor);
```

Main page script, which runs only from a click handler:

```js
async function startConversation(showError) {
  try {
    const ctx = new AudioContext({ sampleRate: 24000 });      // created inside the click: no autoplay warning
    await ctx.audioWorklet.addModule('/js/pcm-worklets.js');
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    });
    const capture = new AudioWorkletNode(ctx, 'capture');
    const player = new AudioWorkletNode(ctx, 'playback');
    ctx.createMediaStreamSource(stream).connect(capture);
    capture.connect(ctx.destination);   // keeps the node pulled; it outputs silence
    player.connect(ctx.destination);

    const ws = new WebSocket(`wss://${location.host}/ws/voice`);   // HttpOnly session cookie goes with the handshake
    ws.binaryType = 'arraybuffer';
    capture.port.onmessage = (e) => { if (ws.readyState === WebSocket.OPEN) ws.send(e.data); };
    ws.onmessage = (e) => {
      if (typeof e.data !== 'string') { player.port.postMessage(e.data, [e.data]); return; } // PCM16 24 kHz
      const msg = JSON.parse(e.data);
      if (msg.type === 'clear') player.port.postMessage(null);   // barge-in flush
      // other control messages: transcript lines, call ended, errors -> UI
    };
    ws.onclose = () => { stream.getTracks().forEach((t) => t.stop()); ctx.close(); };
    ws.onerror = () => showError('Connection lost.');
  } catch (err) {
    showError(err && err.name === 'NotAllowedError' ? 'Microphone access was denied.' : 'Could not start audio.');
  }
}
```

On the server, the browser channel does three things:

- Binary frames from the browser go to `SendInputAudioAsync`.
- `response.audio.delta` bytes go back as binary frames.
- `speech_started` sends a text frame `{"type":"clear"}`.

Set `WebSocketOptions.AllowedOrigins` to our own origin (verified property) to block
cross-site WebSocket hijacking. The cookie alone isn't enough, because browsers send
cookies on cross-origin WebSocket handshakes.

### C.3 Constraints and gotchas

- **User gesture.** Create or resume the `AudioContext` only inside a click handler.
  `getUserMedia` needs HTTPS, which App Service provides.
- **Echo.** With `echoCancellation: true`, the browser cancels the agent's voice coming
  from the speakers. Voice Live's `server_echo_cancellation` adds a second layer.
  Whether Chrome's echo canceller uses WebAudio output as its reference on every
  platform is **UNVERIFIED**. Headphones make the demo safe either way.
- **Firefox.** It historically threw "Connecting AudioNodes from AudioContexts with
  different sample-rate is currently not supported" when a 48 kHz mic stream was
  connected to a 24 kHz context. Whether current Firefox still does is **UNVERIFIED**.
  Fallback: use the default-rate context and resample in the worklet by linear
  interpolation. Chrome and Edge work with the 24 kHz context; the official sample relies
  on this.
- **Background tabs.** AudioWorklets run on the audio rendering thread, and WebSocket
  events aren't timers, so audio keeps flowing in a hidden tab. Chrome's intensive timer
  throttling applies only after the page is hidden for over 5 minutes **and** silent for
  30 s ([Chrome 88 timer throttling](https://developer.chrome.com/blog/timer-throttling-in-chrome-88)).
  Don't drive audio with `setTimeout` or `requestAnimationFrame`.
- **No deprecated APIs.** No `ScriptProcessorNode`; this matches CLAUDE.md.

---

## D. Azure App Service (Linux) specifics

| Setting | Fact | Source |
|---|---|---|
| WebSockets | Enable **Web sockets** in General settings, `az webapp config set --web-sockets-enabled true` | [configure-common](https://learn.microsoft.com/en-us/azure/app-service/configure-common) |
| WebSocket capacity (Linux) | Free: 5 connections. **All other SKUs: about 50K per instance** | [App Service limits](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#app-service-limits), footnote 7 |
| Always On | Basic tier and above. When off, the app unloads after 20 min idle. When on, the front end pings `/` every 5 min. **Required for the ACS answer path (30 s ring window)** | configure-common, ACS incoming-call best practices |
| Session affinity (ARR) | A cookie, so it only helps browsers. ACS callbacks and media sockets don't keep cookies. With in-memory per-call state, **run one instance** and affinity doesn't matter | configure-common |
| HTTP version | `--http20-enabled`: HTTP/2 over TLS for normal requests. Browsers still open WebSockets over HTTP/1.1 Upgrade. Whether the App Service front end supports WebSockets over HTTP/2 (RFC 8441) is **UNVERIFIED** and not needed | configure-common |
| Request timeout | The documented 230 s limit applies to an HTTP request with no response. A WebSocket that is busy (20 ms audio frames) or sends pings isn't idle. Set `WebSocketOptions.KeepAliveInterval` (for example 30 s) and `KeepAliveTimeout`. The exact idle cutoff for a quiet WebSocket on Linux is **UNVERIFIED** | Well-known App Service front-end limit, not re-checked in this research |
| Health check | Pings your path every 1 min. Healthy means 200-299. After 10 failures (`WEBSITE_HEALTHCHECK_MAXPINGFAILURES` 2-10) the instance leaves the load balancer; after 1 h it is replaced. With a single instance it isn't removed. If you use your own auth, the path must allow anonymous access. Changing the config restarts the app | [Health check](https://learn.microsoft.com/en-us/azure/app-service/monitor-instances-health-check) |
| HTTPS Only | Turn it on. Health check then uses HTTPS. With HTTPS redirects but HTTPS Only off, health check sees 307 and fails | Health check page |
| App settings | Nested keys use `__` (for example `VoiceLive__Endpoint`) | configure-common |

The WebSocket middleware options below compiled on .NET 10:

```csharp
var wsOptions = new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30), KeepAliveTimeout = TimeSpan.FromSeconds(20) };
wsOptions.AllowedOrigins.Add("https://<app>.azurewebsites.net");
app.UseWebSockets(wsOptions);
```

`AllowedOrigins` only affects browser requests. ACS media handshakes are authenticated by
JWT (see B.5).

UNVERIFIED / risks for D:

- The Linux idle timeout for quiet WebSockets.
- Whether the "Web sockets" toggle is strictly required on Linux. Enable it anyway.
- Platform maintenance restarts drop live WebSockets. Accept this and document it as a
  known limitation; restart recovery is a backend state concern.

---

## Implications for our plans

- **The phone channel is probably dead on arrival.** New tenants can't get ACS phone
  numbers or trial numbers (announcement September 2026; sign-ups for retiring services
  close 2026-10-23).
  - First action: check whether the owner's tenant already has an ACS resource with
    numbers.
  - If it doesn't, drop the phone channel and document why (cite the retirement guide)
    instead of building ACS code we can't demo.
  - Keep the channel abstraction anyway: it costs nothing and shows the design.
- **Pin `Azure.AI.VoiceLive` 1.2.0** (GA, default API `2026-07-15`). Use only the class
  names in A.4 to A.9; they all compile. Don't copy 1.2.0-beta.1 or older sample code
  blindly.
- **One `VoiceLiveClient` singleton** with `DefaultAzureCredential`. The managed identity
  gets `Cognitive Services User` + `Foundry User` on the Foundry resource. No keys.
- **Model `gpt-4.1-mini`, region Sweden Central, voice `en-US-Ava:DragonHDLatestNeural`,
  transcription `azure-speech`.** All are config values. Budget about 3 cents per
  conversation-minute. Plan a quick A/B test with `gpt-realtime-mini`, which needs a
  different transcription model.
- **Session settings:** `azure_semantic_vad` with `InterruptResponse = true`,
  `AutoTruncate = true`, `RemoveFillerWords = true`; deep noise suppression; server echo
  cancellation; `AllowParallelToolCalls = false`; capped output tokens. These live in
  the shared session code, never in channel code.
- **Barge-in in our code is only "flush the channel".** The service cancels the response
  itself. Track `responseActive` to coalesce `response.create` after tool outputs and to
  avoid stray `response.cancel` errors.
- **Function calls:** handle only `SessionUpdateResponseFunctionCallArgumentsDone`, reply
  with `FunctionCallOutputItem`, and request the next response after `response.done`.
  This becomes the backend "model proposes, backend decides" choke point.
- **Browser protocol:** binary PCM16 24 kHz frames both ways plus small JSON control
  frames (`clear`, transcript lines, `ended`, `error`). Use one 24 kHz `AudioContext`
  created on click, and two worklets in one static JS file. Restrict
  `WebSocketOptions.AllowedOrigins`.
- **App Service:** Linux, Basic B1 or higher (Always On), WebSockets on, HTTPS Only on,
  a single instance (in-memory session state), health check on an anonymous
  `/healthz`. If ACS ever happens, Always On is mandatory for the 30 s answer window.
- **Telemetry hygiene:** keep the SDK content logging and the GenAI content-capture
  environment variables off, because function arguments carry verification codes.
- **Sessions last at most 60 min, with 100 new connections/min and 120K TPM per
  resource.** Add a backend conversation time limit well under 60 min, for example 10
  min. That is both a guardrail and a cost cap.
