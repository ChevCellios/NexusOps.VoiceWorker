using NexusOps.Web.Models;
using Npgsql;

namespace NexusOps.Web.Services;

public interface IInventoryStore
{
    IReadOnlyList<InventoryStockItem> ListStock();
    IReadOnlyList<InventoryWarehouse> ListWarehouses();
    void RecordMovement(InventoryMovementInput input, string? actor);
    void Transfer(InventoryTransferInput input, string? actor);
}

public sealed class PostgresInventoryStore(NpgsqlDataSource dataSource, Guid tenantId) : IInventoryStore
{
    public IReadOnlyList<InventoryStockItem> ListStock()
    {
        const string sql = "select s.id,w.id,w.name,i.item_code,i.name,i.unit_of_measure,s.quantity,s.reserved_quantity,i.minimum_quantity,i.unit_cost from inventory_stock s join warehouses w on w.id=s.warehouse_id join inventory_items i on i.id=s.item_id where s.tenant_id=$1 order by w.name,i.name";
        using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(tenantId);
        using var reader = command.ExecuteReader();
        var items = new List<InventoryStockItem>();
        while (reader.Read())
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9)));
        return items;
    }

    public IReadOnlyList<InventoryWarehouse> ListWarehouses()
    {
        using var command = dataSource.CreateCommand("select id,name from warehouses where tenant_id=$1 and is_active=true order by name");
        command.Parameters.AddWithValue(tenantId);
        using var reader = command.ExecuteReader();
        var warehouses = new List<InventoryWarehouse>();
        while (reader.Read()) warehouses.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return warehouses;
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
        decimal reservedQuantity;

        using (var stockCommand = new NpgsqlCommand("select warehouse_id,item_id,quantity,reserved_quantity from inventory_stock where id=$1 and tenant_id=$2 for update", connection, transaction))
        {
            stockCommand.Parameters.AddWithValue(input.StockId);
            stockCommand.Parameters.AddWithValue(tenantId);
            using var reader = stockCommand.ExecuteReader();
            if (!reader.Read()) throw new ArgumentException("Stavka skladišta nije pronađena.");
            warehouseId = reader.GetGuid(0);
            itemId = reader.GetGuid(1);
            currentQuantity = reader.GetDecimal(2);
            reservedQuantity = reader.GetDecimal(3);
        }

        var change = input.MovementType == "receipt" ? input.Quantity : -input.Quantity;
        if (currentQuantity + change < reservedQuantity) throw new ArgumentException("Nema dovoljno slobodne robe za evidentiranje izlaza.");

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

    public void Transfer(InventoryTransferInput input, string? actor)
    {
        using var connection = dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Guid sourceWarehouseId;
        Guid itemId;
        decimal quantity;
        decimal reserved;
        using (var sourceCommand = new NpgsqlCommand("select warehouse_id,item_id,quantity,reserved_quantity from inventory_stock where id=$1 and tenant_id=$2 for update", connection, transaction))
        {
            sourceCommand.Parameters.AddWithValue(input.SourceStockId); sourceCommand.Parameters.AddWithValue(tenantId);
            using var reader = sourceCommand.ExecuteReader();
            if (!reader.Read()) throw new ArgumentException("Izvorna stavka skladišta nije pronađena.");
            sourceWarehouseId = reader.GetGuid(0); itemId = reader.GetGuid(1); quantity = reader.GetDecimal(2); reserved = reader.GetDecimal(3);
        }
        if (sourceWarehouseId == input.DestinationWarehouseId) throw new ArgumentException("Odredišno skladište mora biti različito od izvornog.");
        if (quantity - input.Quantity < reserved) throw new ArgumentException("Nema dovoljno slobodne robe za prijenos.");
        using (var destinationCheck = new NpgsqlCommand("select 1 from warehouses where id=$1 and tenant_id=$2 and is_active=true", connection, transaction))
        {
            destinationCheck.Parameters.AddWithValue(input.DestinationWarehouseId); destinationCheck.Parameters.AddWithValue(tenantId);
            if (destinationCheck.ExecuteScalar() is null) throw new ArgumentException("Odredišno skladište nije pronađeno.");
        }
        using (var sourceUpdate = new NpgsqlCommand("update inventory_stock set quantity=quantity-$1,updated_at=now() where id=$2", connection, transaction))
        { sourceUpdate.Parameters.AddWithValue(input.Quantity); sourceUpdate.Parameters.AddWithValue(input.SourceStockId); sourceUpdate.ExecuteNonQuery(); }
        using (var destinationUpdate = new NpgsqlCommand("insert into inventory_stock (tenant_id,warehouse_id,item_id,quantity) values ($1,$2,$3,$4) on conflict (warehouse_id,item_id) do update set quantity=inventory_stock.quantity+excluded.quantity,updated_at=now()", connection, transaction))
        { destinationUpdate.Parameters.AddWithValue(tenantId); destinationUpdate.Parameters.AddWithValue(input.DestinationWarehouseId); destinationUpdate.Parameters.AddWithValue(itemId); destinationUpdate.Parameters.AddWithValue(input.Quantity); destinationUpdate.ExecuteNonQuery(); }
        foreach (var movement in new[] { (sourceWarehouseId, "transfer_out"), (input.DestinationWarehouseId, "transfer_in") })
        using (var movementCommand = new NpgsqlCommand("insert into inventory_movements (tenant_id,warehouse_id,item_id,movement_type,quantity,note,created_by_name) values ($1,$2,$3,$4,$5,$6,$7)", connection, transaction))
        { movementCommand.Parameters.AddWithValue(tenantId); movementCommand.Parameters.AddWithValue(movement.Item1); movementCommand.Parameters.AddWithValue(itemId); movementCommand.Parameters.AddWithValue(movement.Item2); movementCommand.Parameters.AddWithValue(input.Quantity); movementCommand.Parameters.AddWithValue((object?)input.Note?.Trim() ?? DBNull.Value); movementCommand.Parameters.AddWithValue((object?)actor ?? DBNull.Value); movementCommand.ExecuteNonQuery(); }
        transaction.Commit();
    }
}

public sealed class InMemoryInventoryStore : IInventoryStore
{
    private readonly List<InventoryWarehouse> warehouses = [new(Guid.NewGuid(), "Centralno skladište"), new(Guid.NewGuid(), "Tehničko skladište")];
    private readonly List<InventoryStockItem> items;
    public InMemoryInventoryStore() => items = [new(Guid.NewGuid(), warehouses[0].Id, warehouses[0].Name, "INV-001", "Industrijski filter F-400", "kom", 36, 4, 12, 18.5m), new(Guid.NewGuid(), warehouses[1].Id, warehouses[1].Name, "INV-005", "Propeler set za dron", "set", 4, 2, 6, 42m)];

    public IReadOnlyList<InventoryStockItem> ListStock() => items;
    public IReadOnlyList<InventoryWarehouse> ListWarehouses() => warehouses;

    public void RecordMovement(InventoryMovementInput input, string? actor)
    {
        if (input.MovementType is not ("receipt" or "issue")) throw new ArgumentException("Podržani su samo ulaz i izlaz robe.");
        var current = items.SingleOrDefault(x => x.StockId == input.StockId) ?? throw new ArgumentException("Stavka skladišta nije pronađena.");
        var change = input.MovementType == "receipt" ? input.Quantity : -input.Quantity;
        if (current.Quantity + change < current.Reserved) throw new ArgumentException("Nema dovoljno slobodne robe za evidentiranje izlaza.");
        items[items.IndexOf(current)] = current with { Quantity = current.Quantity + change };
    }

    public void Transfer(InventoryTransferInput input, string? actor)
    {
        var source = items.SingleOrDefault(x => x.StockId == input.SourceStockId) ?? throw new ArgumentException("Izvorna stavka skladišta nije pronađena.");
        if (source.WarehouseId == input.DestinationWarehouseId) throw new ArgumentException("Odredišno skladište mora biti različito od izvornog.");
        if (source.Quantity - input.Quantity < source.Reserved) throw new ArgumentException("Nema dovoljno slobodne robe za prijenos.");
        var destinationWarehouse = warehouses.SingleOrDefault(x => x.Id == input.DestinationWarehouseId) ?? throw new ArgumentException("Odredišno skladište nije pronađeno.");
        items[items.IndexOf(source)] = source with { Quantity = source.Quantity - input.Quantity };
        var destination = items.SingleOrDefault(x => x.WarehouseId == destinationWarehouse.Id && x.Code == source.Code);
        if (destination is null) items.Add(source with { StockId = Guid.NewGuid(), WarehouseId = destinationWarehouse.Id, Warehouse = destinationWarehouse.Name, Quantity = input.Quantity, Reserved = 0 });
        else items[items.IndexOf(destination)] = destination with { Quantity = destination.Quantity + input.Quantity };
    }
}
