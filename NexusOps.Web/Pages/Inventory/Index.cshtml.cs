using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Inventory;

public sealed class IndexModel(IInventoryStore store) : PageModel
{
    [BindProperty] public InventoryMovementInput Input { get; set; } = new();
    public IReadOnlyList<InventoryStockItem> Items { get; private set; } = [];

    public void OnGet() => Items = store.ListStock();

    public IActionResult OnPost()
    {
        Items = store.ListStock();
        if (!User.IsInRole("Administrator") && !User.IsInRole("Manager") && !User.IsInRole("Technician")) return Forbid();
        if (!ModelState.IsValid) return Page();

        try
        {
            store.RecordMovement(Input, User.Identity?.Name ?? "Sustav");
            TempData["Success"] = Input.MovementType == "receipt" ? "Ulaz robe je evidentiran." : "Izlaz robe je evidentiran.";
            return RedirectToPage();
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
