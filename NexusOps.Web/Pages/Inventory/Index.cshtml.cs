using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Inventory;

public sealed class IndexModel(IInventoryStore store) : PageModel
{
    [BindProperty] public InventoryMovementInput Input { get; set; } = new();
    [BindProperty] public InventoryTransferInput TransferInput { get; set; } = new();
    public IReadOnlyList<InventoryStockItem> Items { get; private set; } = [];
    public IReadOnlyList<InventoryWarehouse> Warehouses { get; private set; } = [];

    public void OnGet() => LoadData();

    public IActionResult OnPost()
    {
        LoadData();
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

    public IActionResult OnPostTransfer()
    {
        LoadData();
        if (!User.IsInRole("Administrator") && !User.IsInRole("Manager") && !User.IsInRole("Technician")) return Forbid();
        if (!ModelState.IsValid) return Page();
        try
        {
            store.Transfer(TransferInput, User.Identity?.Name ?? "Sustav");
            TempData["Success"] = "Prijenos robe je evidentiran.";
            return RedirectToPage();
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private void LoadData()
    {
        Items = store.ListStock();
        Warehouses = store.ListWarehouses();
    }
}
