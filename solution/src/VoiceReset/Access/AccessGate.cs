using System.Threading.RateLimiting;

namespace VoiceReset.Access;

/// <summary>
/// The access-code gate: cookie scheme "Access", authorization policy "Access" (used by /voice/ws)
/// and the rate limit for code attempts. It protects cost only; it never authorizes a reset.
/// </summary>
public static class AccessGate
{
    public const string Scheme = "Access";
    public const string Policy = "Access";
    public const string CookieName = "__Host-vr-access";   // __Host-: Secure, Path=/, no Domain
    public const string RateLimitPolicy = "access-code";

    public static IServiceCollection AddAccessGate(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.SectionName))
            .Validate(AccessOptions.IsValid, "Access settings are missing or invalid.")
            .ValidateOnStart();

        // The mock inbox registered the authentication builder with no default scheme; every user names its scheme.
        services.AddAuthentication()
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(2);
                options.SlidingExpiration = false;
                // An API, not a login page: answer 401/403 instead of redirecting.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());

        services.AddRateLimiter(options =>
        {
            // Guessing the code: 5 attempts per minute per client IP.
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Only /access is rate limited. A refusal is an expected failure: 200, so the console stays clean.
            options.OnRejected = (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new AccessResult(false, true), ct));
            };
        });

        return services;
    }
}
