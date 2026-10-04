using Azure.AI.VoiceLive;

namespace VoiceReset.Features.Voice;

/// <summary>
/// The Voice Live session settings. They live here, in shared code, so the browser and the phone
/// behave the same: only the audio format depends on the channel.
/// </summary>
public static class VoiceLiveSettings
{
    public static VoiceLiveSessionOptions Build(VoiceLiveOptions settings, string instructions, ChannelAudio audio)
    {
        var phone = audio == ChannelAudio.MuLawAt8kHz;   // Voice Live takes and sends μ-law directly: no conversion here
        var options = new VoiceLiveSessionOptions
        {
            Instructions = instructions,
            Voice = new AzureStandardVoice(settings.Voice),
            InputAudioFormat = phone ? InputAudioFormat.G711Ulaw : InputAudioFormat.Pcm16,
            OutputAudioFormat = phone ? OutputAudioFormat.G711Ulaw : OutputAudioFormat.Pcm16,
            InputAudioSamplingRate = phone ? 8000 : 24000,
            // English only: the transcript the model reasons over is English speech-to-text.
            InputAudioTranscription = new AudioInputTranscriptionOptions(AudioInputTranscriptionOptionsModel.AzureSpeech)
            {
                Language = "en-US",
            },
            TurnDetection = new AzureSemanticVadTurnDetection
            {
                // Spelled usernames lost their first letter in tests: keep more audio from before the speech
                // was detected, and don't drop short sounds as fillers (a spoken "A" can look like "uh").
                PrefixPadding = TimeSpan.FromMilliseconds(1000),   // a probe lost "A" of "A L E X" at 300 ms
                RemoveFillerWords = false,
                InterruptResponse = true,   // barge-in: caller speech cancels the agent's answer
                AutoTruncate = true,        // the history keeps only what the caller actually heard
                CreateResponse = true,      // the service answers at the end of each caller turn
            },
            InputAudioNoiseReduction = new AudioNoiseReduction(AudioNoiseReductionType.AzureDeepNoiseSuppression),
            InputAudioEchoCancellation = new AudioEchoCancellation(),
            ToolChoice = new ToolChoiceOption(ToolChoiceLiteral.Auto),
            AllowParallelToolCalls = false,   // one tool call per response keeps the state machine simple
            MaxResponseOutputTokens = new MaxResponseOutputTokensOption(300),
        };
        options.Modalities.Clear();
        options.Modalities.Add(InteractionModality.Text);
        options.Modalities.Add(InteractionModality.Audio);
        foreach (var tool in ToolDefinitions.All)
        {
            options.Tools.Add(tool);
        }
        return options;
    }
}
