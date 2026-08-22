using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.WorkOrders;
public sealed class DetailsModel(IOperationsStore store, IInventoryStore inventoryStore) : PageModel
{
    public WorkOrder Order { get; private set; } = default!;
    public Asset? Asset { get; private set; }
    public IReadOnlyList<WorkOrderEvent> Events { get; private set; } = [];
    public IReadOnlyList<WorkOrderMaterialUsage> Materials { get; private set; } = [];
    [BindProperty] public Guid Id { get; set; }
    [BindProperty] public WorkOrderStatus Status { get; set; }
    public IActionResult OnGet(Guid id)
    {
        Order = store.GetWorkOrder(id)!;
        if (Order is null) return NotFound();

        Asset = store.ListAssets().FirstOrDefault(item => item.Id == Order.AssetId);
        Events = store.ListWorkOrderEvents(id);
        Materials = inventoryStore.ListWorkOrderMaterialUsage(id);
        Id = id;
        Status = Order.Status;
        return Page();
    }

    public IActionResult OnPost()
    {
        try
        {
            store.UpdateWorkOrderStatus(Id, Status, User.Identity?.Name ?? "Sustav");
            TempData["Success"] = "Status radnog naloga je ažuriran.";
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        return RedirectToPage(new { id = Id });
    }
}
