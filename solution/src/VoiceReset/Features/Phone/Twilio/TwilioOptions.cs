namespace VoiceReset.Features.Phone.Twilio;

/// <summary>Configuration section "Phone:Twilio". Empty token: the phone channel is off.</summary>
public sealed class TwilioOptions
{
    public const string SectionName = "Phone:Twilio";

    /// <summary>The Twilio account's Auth Token; it signs every webhook request. An app setting, never in the repo.</summary>
    public string AuthToken { get; set; } = "";
}
