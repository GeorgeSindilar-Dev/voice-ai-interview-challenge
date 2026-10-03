using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoiceReset.Mock;

namespace VoiceReset.Pages.Mock.Inbox;

public sealed class LoginModel(MockIssuer issuer) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public bool Failed { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = ModelState.IsValid ? issuer.FindUser(Username) : null;
        // Always one constant-time comparison, so an unknown username looks like a wrong password.
        var passwordMatches = MockIssuer.SecretEquals(Password ?? "", user?.InboxPassword ?? "");
        if (user is null || !passwordMatches)
        {
            Failed = true;
            return Page();
        }
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, MockIssuer.Normalize(user.Username)), new Claim(MockInboxAuth.DisplayNameClaim, user.DisplayName)],
            MockInboxAuth.Scheme);
        await HttpContext.SignInAsync(MockInboxAuth.Scheme, new ClaimsPrincipal(identity));
        return RedirectToPage("Index");
    }
}
