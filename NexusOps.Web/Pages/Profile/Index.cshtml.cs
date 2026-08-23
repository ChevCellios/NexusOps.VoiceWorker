using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Security;

namespace NexusOps.Web.Pages.Profile;

public sealed class IndexModel : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public NexusOpsRole Role { get; private set; }
    public string RoleDescription { get; private set; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated != true) return RedirectToPage("/Account/Login");
        Email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity.Name ?? string.Empty;
        Role = Enum.TryParse<NexusOpsRole>(User.FindFirstValue(ClaimTypes.Role), true, out var role) ? role : NexusOpsRole.Viewer;
        (RoleDescription, Permissions) = Role switch
        {
            NexusOpsRole.Administrator => ("Puni pristup organizaciji i upravljanju korisnicima.", (IReadOnlyList<string>)new[] { "Pregled i izmjena poslovnih podataka", "Upravljanje korisnicima i ulogama", "Upravljanje timom, radnim nalozima i financijama" }),
            NexusOpsRole.Manager => ("Operativno upravljanje radom tima i poslovnim zapisima.", (IReadOnlyList<string>)new[] { "Stvaranje i izmjena operativnih zapisa", "Upravljanje timom i statusima", "Dodavanje korisnika u dopuštenim ulogama" }),
            NexusOpsRole.Technician => ("Operativna uloga za izvršavanje radnih naloga.", (IReadOnlyList<string>)new[] { "Pregled poslovnih podataka", "Ažuriranje statusa radnih naloga", "Pregled tima i raspoloživosti" }),
            NexusOpsRole.Demo => ("Javni prikaz bez poslovnih ovlasti.", (IReadOnlyList<string>)new[] { "Pokretanje simulacije telefonskog poziva", "Nema pristupa poslovnim podacima", "Nema stvarnih troškova ili vanjskih poziva" }),
            _ => ("Uloga samo za pregled poslovnih podataka.", (IReadOnlyList<string>)new[] { "Pregled aplikacije", "Bez izmjena zapisa", "Bez upravljanja korisnicima" })
        };
        return Page();
    }
}
