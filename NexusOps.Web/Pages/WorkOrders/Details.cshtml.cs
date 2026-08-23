using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.WorkOrders;
public sealed class DetailsModel(IOperationsStore store, IInventoryStore inventoryStore, ILaborStore laborStore) : PageModel
{
    public WorkOrder Order { get; private set; } = default!;
    public Asset? Asset { get; private set; }
    public IReadOnlyList<WorkOrderEvent> Events { get; private set; } = [];
    public IReadOnlyList<WorkOrderMaterialUsage> Materials { get; private set; } = [];
    public IReadOnlyList<WorkOrderLaborEntry> Labor { get; private set; } = [];
    public IReadOnlyList<LaborEmployee> Employees { get; private set; } = [];
    [BindProperty] public CreateLaborInput LaborInput { get; set; } = new();
    [BindProperty] public Guid Id { get; set; }
    [BindProperty] public WorkOrderStatus Status { get; set; }
    public IActionResult OnGet(Guid id)
    {
        Order = store.GetWorkOrder(id)!;
        if (Order is null) return NotFound();

        Asset = store.ListAssets().FirstOrDefault(item => item.Id == Order.AssetId);
        Events = store.ListWorkOrderEvents(id);
        Materials = inventoryStore.ListWorkOrderMaterialUsage(id);
        Labor = laborStore.List(id); Employees = laborStore.ListEmployees();
        if (LaborInput.StartedAt == default) LaborInput.StartedAt = DateTimeOffset.Now.AddHours(-1);
        if (LaborInput.EndedAt == default) LaborInput.EndedAt = DateTimeOffset.Now;
        if (LaborInput.HourlyRate == 0) LaborInput.HourlyRate = 20m;
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

    public IActionResult OnPostLabor(Guid id){if(!User.IsInRole("Administrator")&&!User.IsInRole("Manager"))return Forbid();try{laborStore.Add(id,LaborInput);TempData["Success"]="Rad je evidentiran.";}catch(ArgumentException e){ModelState.AddModelError(string.Empty,e.Message);return OnGet(id);}return RedirectToPage(new{id});}
}
