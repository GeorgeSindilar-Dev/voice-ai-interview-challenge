namespace VoiceReset.Shared.Http;

/// <summary>Adds the same security headers to every response.</summary>
public static class SecurityHeaders
{
    // No inline script or style anywhere: all pages are static HTML + files from the same origin.
    // 'wss:' is for the voice WebSocket to our own host.
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self' wss:; frame-ancestors 'none'; base-uri 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // OnStarting runs just before the first byte is sent, so later middleware
            // (static files, error handling) cannot remove or replace these headers.
            context.Response.OnStarting(static state =>
            {
                Apply((HttpContext)state);
                return Task.CompletedTask;
            }, context);
            return next(context);
        });

    private static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers["Permissions-Policy"] = "microphone=(self)";

        // The reset page and the inbox show or carry secrets (link, code): never cache them.
        var path = context.Request.Path;
        if (path.StartsWithSegments("/reset") || path.StartsWithSegments("/mock/inbox"))
        {
            headers.CacheControl = "no-store";
        }
    }
}
