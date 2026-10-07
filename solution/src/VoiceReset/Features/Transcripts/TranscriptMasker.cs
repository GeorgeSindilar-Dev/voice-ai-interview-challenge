using System.Text.RegularExpressions;

namespace VoiceReset.Features.Transcripts;

/// <summary>
/// Masks secrets in one transcript turn before it is stored. Best effort: speech can be transcribed in many ways.
/// Order matters: links first (they contain digits), then "password … is/'s/: …" (rest of the turn), then words that
/// mix letters and digits (they look like passwords, never like codes), then digit runs.
/// </summary>
public static partial class TranscriptMasker
{
    private const int MinCodeDigits = 3;
    private const string TwoDigitWords =
        "ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety";
    private const string Digit = "(?:[0-9]+|zero|oh|one|two|three|four|five|six|seven|eight|nine|" + TwoDigitWords + ")";
    private const string Repeat = @"(?:(?:double|triple)\s+)?";

    public static string Mask(string text)
    {
        var masked = MaskPassword(UrlPattern().Replace(text, "[LINK]"));
        return DigitRunPattern().Replace(masked, MaskRun);
    }

    /// <summary>Only what looks like a password: for the caller's own captions, where the code must stay visible.</summary>
    public static string MaskPassword(string text) =>
        MixedWordPattern().Replace(PasswordPattern().Replace(text, "$1 [REDACTED]"), "[REDACTED]");

    /// <summary>A run like "oh four seven, double one two" or "forty-seven eleven" counts its digits; 3 or more → [CODE].</summary>
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
                _ when TwoDigitWordPattern().IsMatch(word) => 2,   // "forty seven" counts 3: masking too much is fine
                _ => 1,
            };
        }
        return digits >= MinCodeDigits ? "[CODE]" : run.Value;
    }

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // "password is", "password's", "password:", "password would be / will be / was", also "pass word", and with words
    // between them in the same sentence ("my password for Alex.Morgan is …"; a dot inside a word doesn't end it).
    // Masks the rest of the turn: masking too much is fine.
    [GeneratedRegex(@"\b(pass\s?word(?:'s\b|(?:[^.!?]|[.!?](?=\S))*?(?:\s(?:is|was|would\s+be|will\s+be)\b|:))).*", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PasswordPattern();

    // A word with both a letter and a digit, like "1234QWSD" or "Summer2026!".
    [GeneratedRegex(@"(?<!\S)(?=\S*\p{L})(?=\S*\d)\S+")]
    private static partial Regex MixedWordPattern();

    [GeneratedRegex("^(?:" + TwoDigitWords + ")$")]
    private static partial Regex TwoDigitWordPattern();

    [GeneratedRegex(@"\b" + Repeat + Digit + @"(?:[\s,.\-]+" + Repeat + Digit + @")*\b", RegexOptions.IgnoreCase)]
    private static partial Regex DigitRunPattern();

    [GeneratedRegex(@"[\s,.\-]+")]
    private static partial Regex RunSeparator();
}
