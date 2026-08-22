namespace NexusOps.Web.Models;

public enum FinancialAccountType { Bank, Cash, Card, Reserve }
public enum FinancialTransactionType { Income, Expense }

public sealed record FinancialAccount(Guid Id, string Name, FinancialAccountType Type, decimal OpeningBalance, decimal CurrentBalance, string Currency);
public sealed record FinancialTransaction(Guid Id, Guid AccountId, string AccountName, FinancialTransactionType Type, decimal Amount, DateOnly OccurredOn, string? Counterparty, string? Description, string Status);
public sealed record FinanceSummary(decimal TotalBalance, decimal IncomeThisMonth, decimal ExpenseThisMonth, int PlannedPayments);

public sealed class CreateFinancialAccountInput
{
    public string Name { get; set; } = string.Empty;
    public FinancialAccountType Type { get; set; } = FinancialAccountType.Bank;
    public decimal OpeningBalance { get; set; }
}

public sealed class CreateFinancialTransactionInput
{
    public Guid AccountId { get; set; }
    public FinancialTransactionType Type { get; set; } = FinancialTransactionType.Expense;
    public decimal Amount { get; set; }
    public DateOnly OccurredOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? Counterparty { get; set; }
    public string? Description { get; set; }
}
