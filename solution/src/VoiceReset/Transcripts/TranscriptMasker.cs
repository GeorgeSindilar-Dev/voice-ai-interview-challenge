using System.Text.RegularExpressions;

namespace VoiceReset.Transcripts;

/// <summary>
/// Masks secrets in one transcript turn before it is stored. Best effort: speech can be transcribed in many ways.
/// Order matters: links first (they contain digits), then "password is/'s/: …" (rest of the turn), then digit runs.
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

    [GeneratedRegex(@"\b(password(?:\s+is\b|'s\b|:)).*", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"\b" + Repeat + Digit + @"(?:[\s,.\-]+" + Repeat + Digit + @")*\b", RegexOptions.IgnoreCase)]
    private static partial Regex DigitRunPattern();

    [GeneratedRegex(@"[\s,.\-]+")]
    private static partial Regex RunSeparator();
}
