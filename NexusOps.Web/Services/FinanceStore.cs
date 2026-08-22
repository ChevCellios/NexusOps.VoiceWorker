using NexusOps.Web.Models;

namespace NexusOps.Web.Services;

public interface IFinanceStore
{
    IReadOnlyList<FinancialAccount> ListAccounts();
    IReadOnlyList<FinancialTransaction> ListTransactions();
    FinanceSummary GetSummary();
    FinancialAccount CreateAccount(CreateFinancialAccountInput input);
    FinancialTransaction CreateTransaction(CreateFinancialTransactionInput input, string? actorName = null);
}

public sealed class InMemoryFinanceStore : IFinanceStore
{
    private readonly List<FinancialAccount> _accounts =
    [
        new(Guid.Parse("20000000-0000-0000-0000-000000000001"), "Glavni poslovni račun", FinancialAccountType.Bank, 12500m, 12500m, "EUR"),
        new(Guid.Parse("20000000-0000-0000-0000-000000000002"), "Blagajna", FinancialAccountType.Cash, 450m, 450m, "EUR")
    ];
    private readonly List<FinancialTransaction> _transactions = [];

    public IReadOnlyList<FinancialAccount> ListAccounts() => _accounts.OrderBy(item => item.Name).ToArray();
    public IReadOnlyList<FinancialTransaction> ListTransactions() => _transactions.OrderByDescending(item => item.OccurredOn).ToArray();
    public FinanceSummary GetSummary()
    {
        var firstDay = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var month = _transactions.Where(item => item.OccurredOn >= firstDay);
        return new(_accounts.Sum(item => item.CurrentBalance), month.Where(item => item.Type == FinancialTransactionType.Income).Sum(item => item.Amount), month.Where(item => item.Type == FinancialTransactionType.Expense).Sum(item => item.Amount), 0);
    }
    public FinancialAccount CreateAccount(CreateFinancialAccountInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Naziv računa je obavezan.");
        var account = new FinancialAccount(Guid.NewGuid(), input.Name.Trim(), input.Type, input.OpeningBalance, input.OpeningBalance, "EUR");
        _accounts.Add(account); return account;
    }
    public FinancialTransaction CreateTransaction(CreateFinancialTransactionInput input, string? actorName = null)
    {
        if (input.AccountId == Guid.Empty || !_accounts.Any(item => item.Id == input.AccountId)) throw new ArgumentException("Odaberi račun.");
        if (input.Amount <= 0) throw new ArgumentException("Iznos mora biti veći od nule.");
        var account = _accounts.Single(item => item.Id == input.AccountId);
        var change = input.Type == FinancialTransactionType.Income ? input.Amount : -input.Amount;
        _accounts[_accounts.IndexOf(account)] = account with { CurrentBalance = account.CurrentBalance + change };
        var transaction = new FinancialTransaction(Guid.NewGuid(), account.Id, account.Name, input.Type, input.Amount, input.OccurredOn, input.Counterparty?.Trim(), input.Description?.Trim(), "recorded");
        _transactions.Add(transaction); return transaction;
    }
}
