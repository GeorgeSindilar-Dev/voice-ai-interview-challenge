using Microsoft.AspNetCore.HttpOverrides;
using VoiceReset.Access;
using VoiceReset.Health;
using VoiceReset.Http;
using VoiceReset.Mock;
using VoiceReset.Observability;
using VoiceReset.Recovery;
using VoiceReset.Storage;
using VoiceReset.Transcripts;
using VoiceReset.Voice;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddObservability(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddJsonStore(builder.Configuration);
builder.Services.AddTranscripts();
builder.Services.AddOptions<MockOptions>()
    .Bind(builder.Configuration.GetSection(MockOptions.SectionName))
    .Validate(MockOptions.IsValid, "Mock settings are missing or invalid.")
    .ValidateOnStart();
builder.Services.AddSingleton<MockIssuer>();
builder.Services.AddRecovery();
builder.Services.AddTransient<ToolDispatcher>();
builder.Services.AddRazorPages();
builder.Services.AddAuthentication().AddCookie(MockInboxAuth.Scheme, MockInboxAuth.Configure);
builder.Services.AddAuthorization();
builder.Services.AddAccessGate(builder.Configuration);
builder.Services.AddVoice(builder.Configuration);

if (!builder.Environment.IsDevelopment())
{
    // Behind the front end the request may reach us as http: the antiforgery cookie is Secure regardless.
    // (Always is refused on plain http, so local development over http keeps the default.)
    builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);

    // App Service terminates TLS and forwards the original scheme and client address.
    // The app is reachable only through that front end, so every proxy is trusted.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();   // first: the scheme and client address must be right for everything after it
    app.UseHsts();
}

app.UseSecurityHeaders();
app.UseDefaultFiles();   // "/" serves wwwroot/index.html
app.UseStaticFiles();    // public files: the page holds no secrets
app.UseVoiceWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealth();
app.MapAccess();
app.MapMockIssuer();
app.MapMockReset();
app.MapVoice();
app.MapRazorPages();

await app.Services.GetRequiredService<MockIssuer>().LoadAsync(app.Lifetime.ApplicationStopping);
app.Services.GetRequiredService<ITranscriptWriter>();   // fail fast on a storage problem, not on the first call
await app.RunAsync();
