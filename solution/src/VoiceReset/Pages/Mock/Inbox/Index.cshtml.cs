using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoiceReset.Mock;

namespace VoiceReset.Pages.Mock.Inbox;

[Authorize(AuthenticationSchemes = MockInboxAuth.Scheme)]
public sealed class IndexModel(MockIssuer issuer) : PageModel
{
    public IReadOnlyList<InboxMessage> Messages { get; private set; } = [];
    public string DisplayName => User.FindFirst(MockInboxAuth.DisplayNameClaim)?.Value ?? "";

    // The username comes only from the signed-in cookie, never from the request.
    public async Task OnGetAsync(CancellationToken ct) => Messages = await issuer.GetInboxAsync(User.Identity?.Name ?? "", ct);

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(MockInboxAuth.Scheme);
        return RedirectToPage("Login");
    }
}
