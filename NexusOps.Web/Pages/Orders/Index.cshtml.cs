using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Orders;

public sealed class IndexModel(ICustomerOrderStore store) : PageModel
{
    [BindProperty] public CreateCustomerOrderInput Input { get; set; } = new();
    public IReadOnlyList<CustomerOrder> Orders { get; private set; } = [];
    public void OnGet() => Orders = store.ListOrders();
    public IActionResult OnPost()
    {
        Orders = store.ListOrders();
        if (!User.IsInRole("Administrator") && !User.IsInRole("Manager")) return Forbid();
        if (!ModelState.IsValid) return Page();
        try
        {
            var order = store.Receive(Input, User.Identity?.Name ?? "Sustav");
            TempData["Success"] = $"Narudžba {order.Number} je zaprimljena; otvoren je radni nalog {order.WorkOrderNumber}.";
            return RedirectToPage();
        }
        catch (ArgumentException exception) { ModelState.AddModelError(string.Empty, exception.Message); return Page(); }
    }
}
