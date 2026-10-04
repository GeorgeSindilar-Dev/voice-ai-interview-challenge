namespace VoiceReset.Features.Mock;

public sealed class MockOptions
{
    public const string SectionName = "Mock";

    public string ServiceCredential { get; set; } = "";
    public string ResetBaseUrl { get; set; } = "";
    public List<MockUser> Users { get; set; } = [];

    public static bool IsValid(MockOptions options) =>
        options.ServiceCredential.Length >= 16
        && Uri.TryCreate(options.ResetBaseUrl, UriKind.Absolute, out _)
        && options.Users.Count > 0
        && options.Users.All(u => !string.IsNullOrWhiteSpace(u.Username) && !string.IsNullOrWhiteSpace(u.DisplayName)
            && u.InboxPassword.Length > 0 && u.InitialPassword.Length > 0)
        && options.Users.Select(u => MockIssuer.Normalize(u.Username)).Distinct().Count() == options.Users.Count;
}

public sealed class MockUser
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string InboxPassword { get; set; } = "";
    public string InitialPassword { get; set; } = "";
    public bool RequiresUnlock { get; set; }
}
