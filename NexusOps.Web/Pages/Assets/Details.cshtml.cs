using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Assets;

public sealed class DetailsModel(IOperationsStore store) : PageModel
{
    public Asset Asset { get; private set; } = default!;
    public IReadOnlyList<WorkOrder> WorkOrders { get; private set; } = [];
    [BindProperty] public Guid Id { get; set; }
    [BindProperty] public UpdateAssetInput Input { get; set; } = new();

    public IActionResult OnGet(Guid id)
    {
        if (!Load(id, initializeInput: true)) return NotFound();
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!Load(Id, initializeInput: false)) return NotFound();
        if (!ModelState.IsValid) return Page();
        try
        {
            store.UpdateAsset(Id, Input);
            TempData["Success"] = "Podaci stroja su ažurirani.";
            return RedirectToPage(new { id = Id });
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return Page();
        }
    }

    private bool Load(Guid id, bool initializeInput)
    {
        var asset = store.GetAsset(id);
        if (asset is null) return false;
        Asset = asset;
        WorkOrders = store.ListWorkOrders().Where(item => item.AssetId == id).ToArray();
        Id = id;
        if (initializeInput)
            Input = new UpdateAssetInput { Location = asset.Location, Status = asset.Status };
        return true;
    }
}
