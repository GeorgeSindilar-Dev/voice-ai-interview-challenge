using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace VoiceReset.Access;

public sealed record AccessRequest(string? Code);

public sealed record AccessResult(bool Ok, bool TooManyAttempts = false);

public sealed record AccessStatus(bool SignedIn);

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccess(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/access");
        group.MapPost("", SignInAsync).RequireRateLimiting(AccessGate.RateLimitPolicy);
        group.MapGet("/status", (Delegate)GetStatusAsync);   // cast: a route handler, not a RequestDelegate (its result is written)
        return endpoints;
    }

    /// <summary>
    /// Exchanges the access code (JSON body, never the URL) for the access cookie. A wrong code is an
    /// expected failure: 200 with ok=false, so the page shows a message and the console stays clean.
    /// </summary>
    public static async Task<Ok<AccessResult>> SignInAsync(AccessRequest request, HttpContext context, IOptions<AccessOptions> options)
    {
        if (!CodeMatches(request.Code, options.Value.Code))
        {
            return TypedResults.Ok(new AccessResult(false));
        }

        // The cookie only says "passed the gate": no name, no personal data.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "agent-page")], AccessGate.Scheme);
        await context.SignInAsync(AccessGate.Scheme, new ClaimsPrincipal(identity));
        return TypedResults.Ok(new AccessResult(true));
    }

    public static async Task<Ok<AccessStatus>> GetStatusAsync(HttpContext context)
    {
        var result = await context.AuthenticateAsync(AccessGate.Scheme);
        return TypedResults.Ok(new AccessStatus(result.Succeeded));
    }

    /// <summary>Constant time; hashing first makes both sides the same length.</summary>
    public static bool CodeMatches(string? supplied, string expected) =>
        supplied is not null
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
