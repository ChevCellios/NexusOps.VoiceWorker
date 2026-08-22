using NexusOps.Web.Models;
using Npgsql;

namespace NexusOps.Web.Services;

public sealed class PostgresFinanceStore(NpgsqlDataSource dataSource, Guid tenantId) : IFinanceStore
{
    public IReadOnlyList<FinancialAccount> ListAccounts()
    {
        const string sql = "select account_id, account_name, currency, current_balance from financial_account_balances where tenant_id=$1 order by account_name";
        using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(tenantId); using var reader = command.ExecuteReader(); var accounts = new List<FinancialAccount>();
        while (reader.Read()) accounts.Add(new(reader.GetGuid(0), reader.GetString(1), FinancialAccountType.Bank, 0, reader.GetDecimal(3), reader.GetString(2).Trim()));
        return accounts;
    }
    public IReadOnlyList<FinancialTransaction> ListTransactions()
    {
        const string sql = "select transactions.id, transactions.account_id, accounts.name, transactions.transaction_type, transactions.amount, transactions.occurred_on, transactions.counterparty, transactions.description, transactions.status from financial_transactions transactions join financial_accounts accounts on accounts.id=transactions.account_id where transactions.tenant_id=$1 order by transactions.occurred_on desc, transactions.created_at desc limit 50";
        using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(tenantId); using var reader = command.ExecuteReader(); var items = new List<FinancialTransaction>();
        while (reader.Read()) items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), Enum.Parse<FinancialTransactionType>(reader.GetString(3), true), reader.GetDecimal(4), DateOnly.FromDateTime(reader.GetDateTime(5)), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8)));
        return items;
    }
    public FinanceSummary GetSummary()
    {
        using var command = dataSource.CreateCommand("select coalesce(sum(current_balance),0) from financial_account_balances where tenant_id=$1"); command.Parameters.AddWithValue(tenantId); var balance = (decimal)(command.ExecuteScalar() ?? 0m);
        using var monthly = dataSource.CreateCommand("select coalesce(sum(amount) filter (where transaction_type='income'),0), coalesce(sum(amount) filter (where transaction_type='expense'),0) from financial_transactions where tenant_id=$1 and status in ('recorded','paid') and occurred_on >= date_trunc('month',current_date)"); monthly.Parameters.AddWithValue(tenantId); using var reader = monthly.ExecuteReader(); reader.Read();
        return new(balance, reader.GetDecimal(0), reader.GetDecimal(1), 0);
    }
    public FinancialAccount CreateAccount(CreateFinancialAccountInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Naziv računa je obavezan.");
        using var command = dataSource.CreateCommand("insert into financial_accounts (tenant_id,name,account_type,opening_balance) values ($1,$2,$3,$4) returning id,name,account_type,opening_balance,currency"); command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(input.Name.Trim()); command.Parameters.AddWithValue(input.Type.ToString().ToLowerInvariant()); command.Parameters.AddWithValue(input.OpeningBalance); using var reader=command.ExecuteReader(); reader.Read(); return new(reader.GetGuid(0),reader.GetString(1),Enum.Parse<FinancialAccountType>(reader.GetString(2),true),reader.GetDecimal(3),reader.GetDecimal(3),reader.GetString(4).Trim());
    }
    public FinancialTransaction CreateTransaction(CreateFinancialTransactionInput input, string? actorName = null)
    {
        if (input.AccountId == Guid.Empty) throw new ArgumentException("Odaberi račun."); if (input.Amount <= 0) throw new ArgumentException("Iznos mora biti veći od nule.");
        using var command=dataSource.CreateCommand("insert into financial_transactions (tenant_id,account_id,transaction_type,amount,occurred_on,counterparty,description,created_by_name) values ($1,$2,$3,$4,$5,$6,$7,$8) returning id,status"); command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(input.AccountId); command.Parameters.AddWithValue(input.Type.ToString().ToLowerInvariant()); command.Parameters.AddWithValue(input.Amount); command.Parameters.AddWithValue(input.OccurredOn.ToDateTime(TimeOnly.MinValue)); command.Parameters.AddWithValue((object?)input.Counterparty?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue((object?)input.Description?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue((object?)actorName ?? DBNull.Value); using var reader=command.ExecuteReader(); reader.Read(); var account=ListAccounts().FirstOrDefault(item=>item.Id==input.AccountId) ?? throw new ArgumentException("Račun nije pronađen."); return new(reader.GetGuid(0),account.Id,account.Name,input.Type,input.Amount,input.OccurredOn,input.Counterparty?.Trim(),input.Description?.Trim(),reader.GetString(1));
    }
}
