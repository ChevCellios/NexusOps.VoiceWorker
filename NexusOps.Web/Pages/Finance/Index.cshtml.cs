using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;

namespace NexusOps.Web.Pages.Finance;

public sealed class IndexModel(IFinanceStore financeStore) : PageModel
{
    [BindProperty] public CreateFinancialAccountInput Account { get; set; } = new();
    [BindProperty] public CreateFinancialTransactionInput Transaction { get; set; } = new();
    public IReadOnlyList<FinancialAccount> Accounts { get; private set; } = [];
    public IReadOnlyList<FinancialTransaction> Transactions { get; private set; } = [];
    public FinanceSummary Summary { get; private set; } = new(0, 0, 0, 0);

    public void OnGet() => Load();
    public IActionResult OnPostCreateAccount()
    {
        if (!CanManage()) return RedirectToPage("/Account/AccessDenied");
        try { financeStore.CreateAccount(Account); TempData["Success"] = "Financijski račun je dodan."; return RedirectToPage(); }
        catch (ArgumentException exception) { TempData["Error"] = exception.Message; return RedirectToPage(); }
    }
    public IActionResult OnPostCreateTransaction()
    {
        if (!CanManage()) return RedirectToPage("/Account/AccessDenied");
        try { financeStore.CreateTransaction(Transaction, User.Identity?.Name); TempData["Success"] = "Transakcija je evidentirana."; return RedirectToPage(); }
        catch (ArgumentException exception) { TempData["Error"] = exception.Message; return RedirectToPage(); }
    }
    private void Load() { Accounts = financeStore.ListAccounts(); Transactions = financeStore.ListTransactions(); Summary = financeStore.GetSummary(); }
    private bool CanManage() => User.IsInRole("Administrator") || User.IsInRole("Manager");
}
