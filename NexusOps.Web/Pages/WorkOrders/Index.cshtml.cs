using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.WorkOrders;
public sealed class IndexModel(IOperationsStore store) : PageModel { public IReadOnlyList<WorkOrder> WorkOrders { get; private set; }=[]; public void OnGet()=>WorkOrders=store.ListWorkOrders(); }
