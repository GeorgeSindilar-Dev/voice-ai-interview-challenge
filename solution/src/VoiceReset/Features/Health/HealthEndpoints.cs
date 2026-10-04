using System.Reflection;

namespace VoiceReset.Features.Health;

public static class HealthEndpoints
{
    private const string Unknown = "unknown";

    private static readonly string s_commit = CommitFrom(
        typeof(HealthEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => new HealthResponse("ok", s_commit));
        return endpoints;
    }

    /// <summary>Returns the part of an informational version after '+' (the commit SHA), or "unknown".</summary>
    public static string CommitFrom(string? informationalVersion)
    {
        var parts = informationalVersion?.Split('+', 2);
        return parts is [_, { Length: > 0 } commit] ? commit : Unknown;
    }

    private sealed record HealthResponse(string Status, string Commit);
}
