using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages;

public class IndexModel(IOperationsStore store, IFinanceStore financeStore, IInventoryStore inventoryStore) : PageModel
{
    public DashboardSummary Summary { get; private set; } = new(0, 0, 0, 0);
    public FinanceSummary Finance { get; private set; } = new(0, 0, 0, 0);
    public IReadOnlyList<WorkOrder> WorkOrders { get; private set; } = [];
    public IReadOnlyList<DashboardMonth> Months { get; private set; } = [];
    public int LowStockItems { get; private set; }

    public void OnGet()
    {
        Summary = store.GetSummary();
        Finance = financeStore.GetSummary();
        WorkOrders = store.ListWorkOrders().Take(6).ToArray();
        LowStockItems = inventoryStore.ListStock().Count(item => item.Available <= item.Minimum);

        var transactions = financeStore.ListTransactions();
        var rawMonths = Enumerable.Range(0, 6).Select(offset =>
        {
            var date = DateTime.Today.AddMonths(offset - 5);
            var rows = transactions.Where(item => item.OccurredOn.Year == date.Year && item.OccurredOn.Month == date.Month);
            return new { Label = date.ToString("MMM", new System.Globalization.CultureInfo("hr-HR")), Income = rows.Where(item => item.Type == FinancialTransactionType.Income).Sum(item => item.Amount), Expense = rows.Where(item => item.Type == FinancialTransactionType.Expense).Sum(item => item.Amount) };
        }).ToArray();
        var maximum = Math.Max(1m, rawMonths.Max(item => Math.Max(item.Income, item.Expense)));
        Months = rawMonths.Select(item => new DashboardMonth(item.Label, item.Income, item.Expense, (int)Math.Round(item.Income / maximum * 100), (int)Math.Round(item.Expense / maximum * 100))).ToArray();
    }
}

public sealed record DashboardMonth(string Label, decimal Income, decimal Expense, int IncomeHeight, int ExpenseHeight);
