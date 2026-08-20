using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.Assets;
public sealed class IndexModel(IOperationsStore store) : PageModel { public IReadOnlyList<Asset> Assets { get; private set; }=[]; public void OnGet()=>Assets=store.ListAssets(); }
