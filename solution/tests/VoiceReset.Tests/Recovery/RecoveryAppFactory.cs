using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using VoiceReset.Mock;
using VoiceReset.Recovery;
using VoiceReset.Storage;

namespace VoiceReset.Tests.Recovery;

/// <summary>The real app (Development settings) with the in-process mock; the issuer client goes through the test server.</summary>
public sealed class RecoveryAppFactory(InMemoryJsonStore? store = null) : WebApplicationFactory<Program>
{
    public const string Username = "alex.morgan"; // appsettings.Development.json

    public InMemoryJsonStore Store { get; } = store ?? new InMemoryJsonStore();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public RecoveryWorkflow Workflow => Services.GetRequiredService<RecoveryWorkflow>();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IJsonStore>(Store);
            services.AddSingleton<TimeProvider>(Time);
            services.AddHttpClient<IssuerClient>()
                .ConfigurePrimaryHttpMessageHandler(sp => ((TestServer)sp.GetRequiredService<IServer>()).CreateHandler());
        });

    public async Task<string> StartRecoveryAsync(string spokenUsername, CancellationToken ct)
    {
        var id = await Workflow.StartSessionAsync("test", ct);
        await Workflow.StartRecoveryAsync(id, spokenUsername, ct);
        return id;
    }

    public Task<CallSession?> SessionAsync(string id, CancellationToken ct) => Services.GetRequiredService<SessionStore>().GetAsync(id, ct);

    // What the caller reads in the inbox page: the newest code and the newest link.
    public async Task<string> InboxCodeAsync(CancellationToken ct) =>
        Regex.Match((await InboxAsync(ct)).First(m => m.Link is null).Body, @"\b[0-9]{6}\b").Value;

    public async Task<string> InboxLinkAsync(CancellationToken ct) => (await InboxAsync(ct)).First(m => m.Link is not null).Link ?? "";

    // What the caller does in the browser form.
    public async Task CompleteResetAsync(string link, CancellationToken ct)
    {
        const string TokenMarker = "#token=";
        var token = Uri.UnescapeDataString(link[(link.IndexOf(TokenMarker, StringComparison.Ordinal) + TokenMarker.Length)..]);
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/mock/v1/resets",
            new { token, new_password = "Blue-Harbor-Lantern-42", operation_id = Guid.NewGuid().ToString("N") }, ct);
        response.EnsureSuccessStatusCode();
    }

    private Task<IReadOnlyList<InboxMessage>> InboxAsync(CancellationToken ct) =>
        Services.GetRequiredService<MockIssuer>().GetInboxAsync(Username, ct);
}
