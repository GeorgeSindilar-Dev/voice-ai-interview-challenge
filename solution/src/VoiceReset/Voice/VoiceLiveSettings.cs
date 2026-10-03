using Azure.AI.VoiceLive;

namespace VoiceReset.Voice;

/// <summary>
/// The Voice Live session settings. They live here, in shared code, so the browser and the phone
/// behave the same: a channel never changes them.
/// </summary>
public static class VoiceLiveSettings
{
    public const int SampleRate = 24000;

    public static VoiceLiveSessionOptions Build(VoiceLiveOptions settings, string instructions)
    {
        var options = new VoiceLiveSessionOptions
        {
            Instructions = instructions,
            Voice = new AzureStandardVoice(settings.Voice),
            InputAudioFormat = InputAudioFormat.Pcm16,
            OutputAudioFormat = OutputAudioFormat.Pcm16,
            InputAudioSamplingRate = SampleRate,
            // English only: the transcript the model reasons over is English speech-to-text.
            InputAudioTranscription = new AudioInputTranscriptionOptions(AudioInputTranscriptionOptionsModel.AzureSpeech)
            {
                Language = "en-US",
            },
            TurnDetection = new AzureSemanticVadTurnDetection
            {
                RemoveFillerWords = true,   // "uh", "mm" don't interrupt the agent
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
