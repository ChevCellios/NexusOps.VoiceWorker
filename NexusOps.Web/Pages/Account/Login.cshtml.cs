using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Security;

namespace NexusOps.Web.Pages.Account;

public sealed class LoginModel(SupabaseSignInService signInService) : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect(ReturnUrl ?? "/") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ModelState.AddModelError(string.Empty, "Unesi e-mail i lozinku.");
            return Page();
        }

        var result = await signInService.SignInAsync(Email.Trim(), Password, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()),
            new Claim(ClaimTypes.Name, result.Email ?? Email),
            new Claim(ClaimTypes.Email, result.Email ?? Email),
            new Claim(ClaimTypes.Role, result.Role!.Value.ToString())
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return LocalRedirect(ReturnUrl ?? "/");
    }
}
