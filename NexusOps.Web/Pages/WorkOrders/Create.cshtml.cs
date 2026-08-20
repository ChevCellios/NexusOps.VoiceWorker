using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.WorkOrders;
public sealed class CreateModel(IOperationsStore store) : PageModel
{
    [BindProperty] public CreateWorkOrderInput Input { get; set; } = new();
    public IReadOnlyList<Asset> Assets { get; private set; }=[];
    public void OnGet()=>Assets=store.ListAssets();
    public IActionResult OnPost(){ store.CreateWorkOrder(Input); return RedirectToPage("Index"); }
}
