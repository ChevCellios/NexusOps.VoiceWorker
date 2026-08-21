using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.WorkOrders;
public sealed class DetailsModel(IOperationsStore store) : PageModel
{
    public WorkOrder Order { get; private set; } = default!;
    [BindProperty] public Guid Id { get; set; }
    [BindProperty] public WorkOrderStatus Status { get; set; }
    public IActionResult OnGet(Guid id){ Order=store.GetWorkOrder(id)!; if(Order is null)return NotFound(); Id=id; Status=Order.Status; return Page(); }
    public IActionResult OnPost(){ store.UpdateWorkOrderStatus(Id, Status, "Administrator"); return RedirectToPage(new { id=Id }); }
}
