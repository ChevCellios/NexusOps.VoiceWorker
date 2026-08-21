using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.Assets;
public sealed class CreateModel(IOperationsStore store) : PageModel
{
    [BindProperty] public CreateAssetInput Input { get; set; } = new();
    public void OnGet() { }
    public IActionResult OnPost(){ store.CreateAsset(Input); return RedirectToPage("Index"); }
}
