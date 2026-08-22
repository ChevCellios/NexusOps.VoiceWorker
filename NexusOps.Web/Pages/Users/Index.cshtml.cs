using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Security;

namespace NexusOps.Web.Pages.Users;

public sealed class IndexModel(SupabaseSignInService signInService, IUserRoleStore userRoleStore) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Unesi e-mail korisnika.")]
    [EmailAddress(ErrorMessage = "Unesi ispravan e-mail.")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Unesi početnu lozinku.")]
    [MinLength(8, ErrorMessage = "Lozinka mora imati najmanje 8 znakova.")]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public NexusOpsRole Role { get; set; } = NexusOpsRole.Technician;

    public IReadOnlyList<NexusOpsUserAccess> Users { get; private set; } = [];
    public IReadOnlyList<NexusOpsRole> AssignableRoles => IsAdministrator()
        ? [NexusOpsRole.Viewer, NexusOpsRole.Technician, NexusOpsRole.Manager, NexusOpsRole.Administrator]
        : [NexusOpsRole.Viewer, NexusOpsRole.Technician];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!CanManageUsers()) return RedirectToPage("/Account/AccessDenied");
        Users = await userRoleStore.ListAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!CanManageUsers()) return RedirectToPage("/Account/AccessDenied");
        if (!ModelState.IsValid)
        {
            Users = await userRoleStore.ListAsync(cancellationToken);
            return Page();
        }

        if (!CanAssignRole(Role))
        {
            ModelState.AddModelError(string.Empty, "Nemaš ovlast dodijeliti tu ulogu.");
            Users = await userRoleStore.ListAsync(cancellationToken);
            return Page();
        }

        var result = await signInService.CreateUserAsync(Email.Trim(), Password, Role, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Korisnik nije stvoren.");
            Users = await userRoleStore.ListAsync(cancellationToken);
            return Page();
        }

        TempData["Success"] = $"Korisnik {result.Email} je stvoren i dodijeljena mu je uloga {result.Role}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid userId, NexusOpsRole role, bool isActive, CancellationToken cancellationToken)
    {
        if (!CanManageUsers()) return RedirectToPage("/Account/AccessDenied");
        var users = await userRoleStore.ListAsync(cancellationToken);
        var existing = users.FirstOrDefault(user => user.UserId == userId);
        if (existing is null || !CanAssignRole(role) || (!IsAdministrator() && existing.Role >= NexusOpsRole.Manager))
        {
            TempData["Error"] = "Promjena korisnika nije dopuštena.";
            return RedirectToPage();
        }

        if (User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value == userId.ToString())
        {
            TempData["Error"] = "Ne možeš mijenjati vlastiti pristup na ovom ekranu.";
            return RedirectToPage();
        }

        await userRoleStore.UpdateAsync(userId, role, isActive, cancellationToken);
        TempData["Success"] = $"Pristup korisnika {existing.Email ?? userId.ToString()} je ažuriran.";
        return RedirectToPage();
    }

    private bool CanManageUsers() => User.IsInRole(NexusOpsRole.Administrator.ToString()) || User.IsInRole(NexusOpsRole.Manager.ToString());
    private bool IsAdministrator() => User.IsInRole(NexusOpsRole.Administrator.ToString());
    private bool CanAssignRole(NexusOpsRole role) => AssignableRoles.Contains(role);
}
