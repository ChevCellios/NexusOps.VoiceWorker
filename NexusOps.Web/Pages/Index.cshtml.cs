using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages;

public class IndexModel(IOperationsStore store) : PageModel
{
    public DashboardSummary Summary { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<WorkOrder> WorkOrders { get; private set; } = [];

    public void OnGet()
    {
        Summary = store.GetSummary();
        WorkOrders = store.ListWorkOrders().Take(6).ToArray();
    }
}
