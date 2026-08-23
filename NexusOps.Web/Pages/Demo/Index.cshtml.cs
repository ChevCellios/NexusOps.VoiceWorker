using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Demo;

public sealed class IndexModel(IDemoNotificationStore notificationStore) : PageModel
{
    public IReadOnlyList<DemoNotification> Notifications { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (!User.IsInRole("Demo")) return Forbid();
        Notifications = notificationStore.List();
        return Page();
    }

    public IActionResult OnPostStartCall()
    {
        if (!User.IsInRole("Demo")) return Forbid();
        notificationStore.Send(new CreateDemoNotificationInput
        {
            Channel = DemoChannel.Call,
            Recipient = "Demo visitor",
            Message = "NexusOps demo call: Work order RN-DEMO-001, inspection of the automated packaging line, has been assigned. This is a simulation; no real call was placed."
        });
        TempData["Success"] = "Demo Twilio poziv je simuliran. Stvarni poziv nije pokrenut.";
        return RedirectToPage();
    }
}
