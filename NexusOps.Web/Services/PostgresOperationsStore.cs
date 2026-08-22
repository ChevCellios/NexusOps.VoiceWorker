using NexusOps.Web.Models;
using Npgsql;

namespace NexusOps.Web.Services;

public sealed class PostgresOperationsStore(NpgsqlDataSource dataSource, Guid tenantId) : IOperationsStore
{
    public IReadOnlyList<Asset> ListAssets()
    {
        const string sql = "select id, name, asset_code, coalesce(location, ''), status from assets where tenant_id = $1 order by asset_code";
        return ReadAssets(sql);
    }

    public Asset? GetAsset(Guid id) => ListAssets().FirstOrDefault(asset => asset.Id == id);

    public Asset CreateAsset(CreateAssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code)) throw new ArgumentException("Naziv i šifra stroja su obavezni.");
        using var command = dataSource.CreateCommand("insert into assets (tenant_id, asset_code, name, location) values ($1,$2,$3,$4) returning id, name, asset_code, coalesce(location, ''), status");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(input.Code.Trim()); command.Parameters.AddWithValue(input.Name.Trim()); command.Parameters.AddWithValue((object?)input.Location?.Trim() ?? DBNull.Value);
        using var reader = command.ExecuteReader(); reader.Read();
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), ParseAssetStatus(reader.GetString(4)));
    }

    public void UpdateAsset(Guid id, UpdateAssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Location)) throw new ArgumentException("Lokacija stroja je obavezna.");
        using var command = dataSource.CreateCommand("update assets set location=$3, status=$4, updated_at=now() where id=$1 and tenant_id=$2");
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(input.Location.Trim()); command.Parameters.AddWithValue(ToDatabase(input.Status));
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException("Stroj nije pronađen.");
    }

    public IReadOnlyList<WorkOrder> ListWorkOrders()
    {
        const string sql = "select id, work_order_number, title, asset_id, priority, status, coalesce(assigned_to_name, ''), created_at, due_at, description from work_orders where tenant_id = $1 order by created_at desc";
        using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(tenantId);
        using var reader = command.ExecuteReader(); var orders = new List<WorkOrder>();
        while (reader.Read()) orders.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3), ParsePriority(reader.GetString(4)), ParseStatus(reader.GetString(5)), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return orders;
    }

    public WorkOrder? GetWorkOrder(Guid id) => ListWorkOrders().FirstOrDefault(order => order.Id == id);

    public IReadOnlyList<WorkOrderEvent> ListWorkOrderEvents(Guid workOrderId)
    {
        const string sql = "select id, work_order_id, event_type, message, actor_name, created_at from work_order_events where tenant_id=$1 and work_order_id=$2 order by created_at desc";
        using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(workOrderId);
        using var reader = command.ExecuteReader(); var events = new List<WorkOrderEvent>();
        while (reader.Read()) events.Add(new WorkOrderEvent(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        return events;
    }

    public void UpdateWorkOrderStatus(Guid id, WorkOrderStatus status, string? actorName)
    {
        using var command = dataSource.CreateCommand("update work_orders set status=$3, completed_at=case when $3='completed' then now() else completed_at end, updated_at=now() where id=$1 and tenant_id=$2");
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(status == WorkOrderStatus.WaitingParts ? "waiting_parts" : status.ToString().ToLowerInvariant());
        if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException("Radni nalog nije pronađen.");
        using var eventCommand = dataSource.CreateCommand("insert into work_order_events (tenant_id, work_order_id, event_type, message, actor_name) values ($1,$2,'status_changed',$3,$4)");
        eventCommand.Parameters.AddWithValue(tenantId); eventCommand.Parameters.AddWithValue(id); eventCommand.Parameters.AddWithValue($"Status promijenjen u {status}."); eventCommand.Parameters.AddWithValue((object?)actorName ?? DBNull.Value); eventCommand.ExecuteNonQuery();
    }

    public DashboardSummary GetSummary()
    {
        using var command = dataSource.CreateCommand("select count(*) filter (where status not in ('completed','cancelled')), count(*) filter (where priority='critical' and status not in ('completed','cancelled')), (select count(*) from assets where tenant_id=$1 and status <> 'operational'), count(*) filter (where status='completed' and date_trunc('month', completed_at)=date_trunc('month', now())) from work_orders where tenant_id=$1");
        command.Parameters.AddWithValue(tenantId); using var reader = command.ExecuteReader(); reader.Read();
        return new((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
    }

    public WorkOrder CreateWorkOrder(CreateWorkOrderInput input, string? actorName = null)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) throw new ArgumentException("Naslov radnog naloga je obavezan.");
        var number = $"RN-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}";
        using var command = dataSource.CreateCommand("insert into work_orders (tenant_id, work_order_number, title, description, asset_id, priority, status, assigned_to_name, due_at) values ($1,$2,$3,$4,$5,$6,'new',$7,$8) returning id, created_at");
        command.Parameters.AddWithValue(tenantId); command.Parameters.AddWithValue(number); command.Parameters.AddWithValue(input.Title.Trim()); command.Parameters.AddWithValue((object?)input.Description?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue(input.AssetId); command.Parameters.AddWithValue(ToDatabase(input.Priority)); command.Parameters.AddWithValue((object?)input.AssignedTo?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue((object?)input.DueAt ?? DBNull.Value);
        using var reader = command.ExecuteReader(); reader.Read();
        var workOrder = new WorkOrder(reader.GetGuid(0), number, input.Title.Trim(), input.AssetId, input.Priority, WorkOrderStatus.New, input.AssignedTo?.Trim() ?? string.Empty, reader.GetFieldValue<DateTimeOffset>(1), input.DueAt, input.Description?.Trim());
        using var eventCommand = dataSource.CreateCommand("insert into work_order_events (tenant_id, work_order_id, event_type, message, actor_name) values ($1,$2,'created','Radni nalog je otvoren putem web aplikacije.',$3)");
        eventCommand.Parameters.AddWithValue(tenantId); eventCommand.Parameters.AddWithValue(workOrder.Id); eventCommand.Parameters.AddWithValue((object?)actorName ?? DBNull.Value); eventCommand.ExecuteNonQuery();
        return workOrder;
    }

    private IReadOnlyList<Asset> ReadAssets(string sql) { using var command=dataSource.CreateCommand(sql); command.Parameters.AddWithValue(tenantId); using var reader=command.ExecuteReader(); var assets=new List<Asset>(); while(reader.Read()) assets.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),ParseAssetStatus(reader.GetString(4)))); return assets; }
    private static WorkOrderPriority ParsePriority(string value) => Enum.Parse<WorkOrderPriority>(value, true);
    private static WorkOrderStatus ParseStatus(string value) => value == "waiting_parts" ? WorkOrderStatus.WaitingParts : Enum.Parse<WorkOrderStatus>(value.Replace("_", ""), true);
    private static AssetStatus ParseAssetStatus(string value) => Enum.Parse<AssetStatus>(value.Replace("_", ""), true);
    private static string ToDatabase(WorkOrderPriority value) => value.ToString().ToLowerInvariant();
    private static string ToDatabase(AssetStatus value) => value switch
    {
        AssetStatus.AttentionRequired => "attention_required",
        AssetStatus.OutOfService => "out_of_service",
        _ => value.ToString().ToLowerInvariant()
    };
}
