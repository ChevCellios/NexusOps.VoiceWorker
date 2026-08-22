using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.WorkOrders;
public sealed class CreateModel(IOperationsStore store) : PageModel
{
    [BindProperty] public CreateWorkOrderInput Input { get; set; } = new();
    public IReadOnlyList<Asset> Assets { get; private set; }=[];
    public void OnGet() => Assets = store.ListAssets();

    public IActionResult OnPost()
    {
        Assets = store.ListAssets();
        if (Input.AssetId == Guid.Empty) ModelState.AddModelError("Input.AssetId", "Odaberi stroj.");
        if (!ModelState.IsValid) return Page();

        try
        {
            var order = store.CreateWorkOrder(Input, User.Identity?.Name ?? "Sustav");
            TempData["Success"] = $"Radni nalog {order.Number} je otvoren.";
            return RedirectToPage("Details", new { id = order.Id });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }
}
