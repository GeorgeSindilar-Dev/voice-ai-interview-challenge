namespace VoiceReset.Features.Mock;

/// <summary>The synthetic password policy. Descriptions are safe to display or speak.</summary>
public static class PasswordPolicy
{
    public const string Version = "2026-10-v1";

    private static readonly (PolicyRule Rule, Func<string, string, bool, bool> Passes)[] s_rules =
    [
        (new("min_length", "Use at least 12 characters."), (password, _, _) => password.Length >= 12),
        (new("uppercase", "Include an uppercase letter."), (password, _, _) => password.Any(char.IsUpper)),
        (new("lowercase", "Include a lowercase letter."), (password, _, _) => password.Any(char.IsLower)),
        (new("digit", "Include a digit."), (password, _, _) => password.Any(char.IsDigit)),
        (new("not_username", "Do not include your username."),
            (password, username, _) => !password.Contains(username, StringComparison.OrdinalIgnoreCase)),
        (new("not_current", "Do not reuse your current password."), (_, _, isCurrent) => !isCurrent),
    ];

    public static IReadOnlyList<PolicyRule> Rules { get; } = [.. s_rules.Select(r => r.Rule)];

    /// <summary>The rules the password breaks; empty means valid.</summary>
    public static IReadOnlyList<PolicyRule> Check(string password, string username, bool isCurrentPassword) =>
        [.. s_rules.Where(r => !r.Passes(password, username, isCurrentPassword)).Select(r => r.Rule)];
}
