using Microsoft.AspNetCore.Authentication.Cookies;

namespace VoiceReset.Mock;

/// <summary>The mock inbox's own sign-in (separate from the service credential and the agent's access cookie).</summary>
public static class MockInboxAuth
{
    public const string Scheme = "MockInbox";
    public const string DisplayNameClaim = "display_name";

    public static void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "__Host-mock-inbox";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/mock/inbox/login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    }
}
