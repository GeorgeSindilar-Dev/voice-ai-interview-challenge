namespace VoiceReset.Features.Voice;

public sealed class LimitsOptions
{
    public const string SectionName = "Limits";
    private const int MinCallSeconds = 60;
    private const int MaxAllowedCallSeconds = 3600;

    /// <summary>A call longer than this is ended politely; after a restart, older open sessions are closed.</summary>
    public int MaxCallSeconds { get; set; } = 600;

    public static bool IsValid(LimitsOptions options) =>
        options.MaxCallSeconds is >= MinCallSeconds and <= MaxAllowedCallSeconds;
}
