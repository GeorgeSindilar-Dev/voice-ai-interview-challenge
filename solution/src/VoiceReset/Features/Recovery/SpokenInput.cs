using System.Text.RegularExpressions;

namespace VoiceReset.Features.Recovery;

/// <summary>Speech-to-text → username or 6-digit code; null when unclear (never guessed).</summary>
public static partial class SpokenInput
{
    private static readonly char[] s_sentencePunctuation = ['.', ',', '?', '!'];

    private static readonly Dictionary<string, string> s_symbols = new()
    {
        ["dot"] = ".", ["period"] = ".", ["underscore"] = "_", ["dash"] = "-", ["hyphen"] = "-",
    };

    private static readonly Dictionary<string, string> s_digits = new()
    {
        ["zero"] = "0", ["oh"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
        ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
    };

    /// <summary>"Alex dot Morgan" → "alex.morgan".</summary>
    public static string? Username(string? spoken) => Normalize(spoken, UsernameSeparators(), s_symbols, UsernamePattern());

    /// <summary>"oh 4 7-1 one two" → "047112"; anything that isn't exactly 6 digits (e.g. "4 7") → null.</summary>
    public static string? Code(string? spoken) => Normalize(spoken, CodeSeparators(), s_digits, CodePattern());

    private static string? Normalize(string? spoken, Regex separators, Dictionary<string, string> words, Regex valid)
    {
        // Speech-to-text may end the turn with sentence punctuation ("alex dot morgan.").
        var raw = (spoken ?? "").Trim().TrimEnd(s_sentencePunctuation).ToLowerInvariant();
        var parts = separators.Split(raw);
        var text = string.Concat(parts.Select(word => words.GetValueOrDefault(word, word)));
        return valid.IsMatch(text) ? text : null;
    }

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex UsernameSeparators();

    [GeneratedRegex(@"[\s,.\-]+")]
    private static partial Regex CodeSeparators();

    // 3 to 64 characters, starting and ending with a letter or digit.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{1,62}[a-z0-9])$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex CodePattern();
}
