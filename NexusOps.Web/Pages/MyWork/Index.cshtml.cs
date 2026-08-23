using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.MyWork;

public sealed class IndexModel(IOperationsStore operationsStore, ITeamStore teamStore) : PageModel
{
    public TeamMember? Employee { get; private set; }
    public IReadOnlyList<WorkOrder> WorkOrders { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (!TryLoadEmployee()) return Page();
        WorkOrders = operationsStore.ListWorkOrders().Where(order => string.Equals(order.AssignedTo, Employee!.Name, StringComparison.OrdinalIgnoreCase)).OrderBy(order => order.Status == WorkOrderStatus.Completed).ThenBy(order => order.DueAt).ToArray();
        return Page();
    }

    public IActionResult OnPostStart()
    {
        if (!TryLoadEmployee()) return Forbid();
        teamStore.StartWork(Employee!.EmployeeId, User.Identity?.Name);
        TempData["Success"] = "Početak rada je evidentiran.";
        return RedirectToPage();
    }

    public IActionResult OnPostEnd()
    {
        if (!TryLoadEmployee()) return Forbid();
        teamStore.EndWork(Employee!.EmployeeId, User.Identity?.Name);
        TempData["Success"] = "Završetak rada je evidentiran.";
        return RedirectToPage();
    }

    private bool TryLoadEmployee()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        Employee = teamStore.GetForAuthUser(userId);
        return Employee is not null;
    }
}
