using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoiceReset.Mock;

namespace VoiceReset.Pages.Mock;

/// <summary>The mock work account sign-in. It only checks the password, so a tester can see the reset worked.</summary>
public sealed class LoginModel(MockIssuer issuer) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public bool? Result { get; private set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Result = ModelState.IsValid && await issuer.CheckPasswordAsync(Username, Password, ct);
        return Page();
    }
}
