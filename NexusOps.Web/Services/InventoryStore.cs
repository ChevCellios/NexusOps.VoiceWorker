using NexusOps.Web.Models;
using Npgsql;

namespace NexusOps.Web.Services;

public interface IInventoryStore
{
    IReadOnlyList<InventoryStockItem> ListStock();
    void RecordMovement(InventoryMovementInput input, string? actor);
}

public sealed class PostgresInventoryStore(NpgsqlDataSource dataSource, Guid tenantId) : IInventoryStore
{
    public IReadOnlyList<InventoryStockItem> ListStock()
    {
        const string sql = "select s.id,w.name,i.item_code,i.name,i.unit_of_measure,s.quantity,s.reserved_quantity,i.minimum_quantity,i.unit_cost from inventory_stock s join warehouses w on w.id=s.warehouse_id join inventory_items i on i.id=s.item_id where s.tenant_id=$1 order by w.name,i.name";
        using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(tenantId);
        using var reader = command.ExecuteReader();
        var items = new List<InventoryStockItem>();
        while (reader.Read())
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8)));
        return items;
    }

    public void RecordMovement(InventoryMovementInput input, string? actor)
    {
        if (input.MovementType is not ("receipt" or "issue"))
            throw new ArgumentException("Podržani su samo ulaz i izlaz robe.");

        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Guid warehouseId;
        Guid itemId;
        decimal currentQuantity;

        using (var stockCommand = new NpgsqlCommand("select warehouse_id,item_id,quantity from inventory_stock where id=$1 and tenant_id=$2 for update", connection, transaction))
        {
            stockCommand.Parameters.AddWithValue(input.StockId);
            stockCommand.Parameters.AddWithValue(tenantId);
            using var reader = stockCommand.ExecuteReader();
            if (!reader.Read()) throw new ArgumentException("Stavka skladišta nije pronađena.");
            warehouseId = reader.GetGuid(0);
            itemId = reader.GetGuid(1);
            currentQuantity = reader.GetDecimal(2);
        }

        var change = input.MovementType == "receipt" ? input.Quantity : -input.Quantity;
        if (currentQuantity + change < 0) throw new ArgumentException("Nema dovoljno robe za evidentiranje izlaza.");

        using (var movementCommand = new NpgsqlCommand("insert into inventory_movements (tenant_id,warehouse_id,item_id,movement_type,quantity,note,created_by_name) values ($1,$2,$3,$4,$5,$6,$7)", connection, transaction))
        {
            movementCommand.Parameters.AddWithValue(tenantId);
            movementCommand.Parameters.AddWithValue(warehouseId);
            movementCommand.Parameters.AddWithValue(itemId);
            movementCommand.Parameters.AddWithValue(input.MovementType);
            movementCommand.Parameters.AddWithValue(input.Quantity);
            movementCommand.Parameters.AddWithValue((object?)input.Note?.Trim() ?? DBNull.Value);
            movementCommand.Parameters.AddWithValue((object?)actor ?? DBNull.Value);
            movementCommand.ExecuteNonQuery();
        }

        using (var updateCommand = new NpgsqlCommand("update inventory_stock set quantity=quantity+$1,updated_at=now() where id=$2", connection, transaction))
        {
            updateCommand.Parameters.AddWithValue(change);
            updateCommand.Parameters.AddWithValue(input.StockId);
            updateCommand.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}

public sealed class InMemoryInventoryStore : IInventoryStore
{
    private readonly List<InventoryStockItem> items =
    [
        new(Guid.NewGuid(), "Centralno skladište", "INV-001", "Industrijski filter F-400", "kom", 36, 4, 12, 18.5m),
        new(Guid.NewGuid(), "Tehničko skladište", "INV-005", "Propeler set za dron", "set", 4, 2, 6, 42m)
    ];

    public IReadOnlyList<InventoryStockItem> ListStock() => items;

    public void RecordMovement(InventoryMovementInput input, string? actor)
    {
        if (input.MovementType is not ("receipt" or "issue")) throw new ArgumentException("Podržani su samo ulaz i izlaz robe.");
        var current = items.SingleOrDefault(x => x.StockId == input.StockId) ?? throw new ArgumentException("Stavka skladišta nije pronađena.");
        var change = input.MovementType == "receipt" ? input.Quantity : -input.Quantity;
        if (current.Quantity + change < 0) throw new ArgumentException("Nema dovoljno robe za evidentiranje izlaza.");
        items[items.IndexOf(current)] = current with { Quantity = current.Quantity + change };
    }
}
