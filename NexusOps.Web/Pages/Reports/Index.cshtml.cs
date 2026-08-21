using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Reports;

public sealed class IndexModel(IOperationsStore store) : PageModel
{
    public DashboardSummary Summary { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<WorkOrder> WorkOrders { get; private set; } = [];
    public IReadOnlyList<Asset> Assets { get; private set; } = [];
    public int OverdueCount { get; private set; }
    public int DueSoonCount { get; private set; }

    [BindProperty(SupportsGet = true)] public WorkOrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public WorkOrderPriority? Priority { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public void OnGet() => LoadReport();

    public IActionResult OnGetExportCsv()
    {
        LoadReport();
        var assetNames = Assets.ToDictionary(item => item.Id, item => $"{item.Code} — {item.Name}");
        var csv = new StringBuilder();
        csv.AppendLine("Broj;Radni nalog;Stroj;Prioritet;Status;Dodijeljeno;Rok;Kreirano");
        foreach (var order in WorkOrders)
        {
            csv.AppendLine(string.Join(";",
                Escape(order.Number), Escape(order.Title),
                Escape(assetNames.GetValueOrDefault(order.AssetId, "Nije povezan")),
                Escape(order.Priority.ToString()), Escape(order.Status.ToString()), Escape(order.AssignedTo),
                Escape(order.DueAt?.LocalDateTime.ToString("dd.MM.yyyy. HH:mm") ?? string.Empty),
                Escape(order.CreatedAt.LocalDateTime.ToString("dd.MM.yyyy. HH:mm"))));
        }

        return File(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"nexusops-radni-nalozi-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private void LoadReport()
    {
        Summary = store.GetSummary();
        Assets = store.ListAssets();
        var query = store.ListWorkOrders().AsEnumerable();
        if (Status is not null) query = query.Where(item => item.Status == Status);
        if (Priority is not null) query = query.Where(item => item.Priority == Priority);
        if (From is not null) query = query.Where(item => item.CreatedAt.LocalDateTime.Date >= From.Value.Date);
        if (To is not null) query = query.Where(item => item.CreatedAt.LocalDateTime.Date <= To.Value.Date);
        WorkOrders = query.ToArray();

        var now = DateTimeOffset.UtcNow;
        OverdueCount = WorkOrders.Count(item => item.Status is not WorkOrderStatus.Completed && item.DueAt is not null && item.DueAt < now);
        DueSoonCount = WorkOrders.Count(item => item.Status is not WorkOrderStatus.Completed && item.DueAt is not null && item.DueAt >= now && item.DueAt <= now.AddDays(2));
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
