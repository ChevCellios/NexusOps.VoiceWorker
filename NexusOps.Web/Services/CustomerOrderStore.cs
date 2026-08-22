using NexusOps.Web.Models;
using Npgsql;

namespace NexusOps.Web.Services;

public interface ICustomerOrderStore
{
    IReadOnlyList<CustomerOrder> ListOrders();
    CustomerOrder Receive(CreateCustomerOrderInput input, string? actor);
}

public sealed class PostgresCustomerOrderStore(NpgsqlDataSource dataSource, Guid tenantId) : ICustomerOrderStore
{
    public IReadOnlyList<CustomerOrder> ListOrders()
    {
        const string sql = "select o.id,o.order_number,o.customer_name,o.customer_reference,o.description,o.priority,o.required_by,o.status,coalesce(o.assigned_to_name,''),coalesce(w.work_order_number,''),o.created_at from customer_orders o left join work_orders w on w.id=o.work_order_id where o.tenant_id=$1 order by o.created_at desc";
        using var command = dataSource.CreateCommand(sql); command.Parameters.AddWithValue(tenantId);
        using var reader = command.ExecuteReader(); var orders = new List<CustomerOrder>();
        while (reader.Read()) orders.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), Enum.Parse<WorkOrderPriority>(reader.GetString(5), true), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10)));
        return orders;
    }

    public CustomerOrder Receive(CreateCustomerOrderInput input, string? actor)
    {
        if (string.IsNullOrWhiteSpace(input.CustomerName) || string.IsNullOrWhiteSpace(input.Description)) throw new ArgumentException("Kupac i opis narudžbe su obavezni.");
        using var connection = dataSource.OpenConnection(); using var transaction = connection.BeginTransaction();
        var assignedTo = FindAvailableWorker(connection, transaction) ?? "Neraspoređeno";
        var orderNumber = $"NAR-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}";
        var workOrderNumber = $"RN-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(100000, 999999)}";
        Guid workOrderId; DateTimeOffset createdAt;
        using (var workOrder = new NpgsqlCommand("insert into work_orders (tenant_id,work_order_number,title,description,priority,status,assigned_to_name,due_at,source) values ($1,$2,$3,$4,$5,'assigned',$6,$7,'web') returning id,created_at", connection, transaction))
        {
            workOrder.Parameters.AddWithValue(tenantId); workOrder.Parameters.AddWithValue(workOrderNumber); workOrder.Parameters.AddWithValue($"Narudžba {orderNumber}: {input.CustomerName.Trim()}"); workOrder.Parameters.AddWithValue(input.Description.Trim()); workOrder.Parameters.AddWithValue(input.Priority.ToString().ToLowerInvariant()); workOrder.Parameters.AddWithValue(assignedTo); workOrder.Parameters.AddWithValue((object?)input.RequiredBy ?? DBNull.Value);
            using var reader = workOrder.ExecuteReader(); reader.Read(); workOrderId = reader.GetGuid(0); createdAt = reader.GetFieldValue<DateTimeOffset>(1);
        }
        using (var audit = new NpgsqlCommand("insert into work_order_events (tenant_id,work_order_id,event_type,message,actor_name) values ($1,$2,'created_from_order',$3,$4)", connection, transaction))
        { audit.Parameters.AddWithValue(tenantId); audit.Parameters.AddWithValue(workOrderId); audit.Parameters.AddWithValue($"Automatski otvoreno iz narudžbe {orderNumber}."); audit.Parameters.AddWithValue((object?)actor ?? DBNull.Value); audit.ExecuteNonQuery(); }
        Guid orderId;
        using (var order = new NpgsqlCommand("insert into customer_orders (tenant_id,order_number,customer_name,customer_reference,description,priority,required_by,work_order_id,assigned_to_name) values ($1,$2,$3,$4,$5,$6,$7,$8,$9) returning id", connection, transaction))
        {
            order.Parameters.AddWithValue(tenantId); order.Parameters.AddWithValue(orderNumber); order.Parameters.AddWithValue(input.CustomerName.Trim()); order.Parameters.AddWithValue((object?)input.CustomerReference?.Trim() ?? DBNull.Value); order.Parameters.AddWithValue(input.Description.Trim()); order.Parameters.AddWithValue(input.Priority.ToString().ToLowerInvariant()); order.Parameters.AddWithValue((object?)input.RequiredBy ?? DBNull.Value); order.Parameters.AddWithValue(workOrderId); order.Parameters.AddWithValue(assignedTo); orderId = (Guid)order.ExecuteScalar()!;
        }
        transaction.Commit();
        return new(orderId, orderNumber, input.CustomerName.Trim(), input.CustomerReference?.Trim(), input.Description.Trim(), input.Priority, input.RequiredBy, "work_order_created", assignedTo, workOrderNumber, createdAt);
    }

    private string? FindAvailableWorker(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        using var command = new NpgsqlCommand("select coalesce(e.full_name,concat(e.first_name,' ',e.last_name)) from employees e left join employee_presence p on p.employee_id=e.id where e.tenant_id=$1 and coalesce(p.presence_status,'off_duty') in ('working','on_site') and coalesce(p.do_not_disturb,false)=false order by p.status_started_at nulls last,e.created_at limit 1", connection, transaction);
        command.Parameters.AddWithValue(tenantId);
        return command.ExecuteScalar() as string;
    }
}

public sealed class InMemoryCustomerOrderStore(IOperationsStore operationsStore) : ICustomerOrderStore
{
    private readonly List<CustomerOrder> orders = [];
    public IReadOnlyList<CustomerOrder> ListOrders() => orders.OrderByDescending(x => x.CreatedAt).ToArray();
    public CustomerOrder Receive(CreateCustomerOrderInput input, string? actor)
    {
        if (string.IsNullOrWhiteSpace(input.CustomerName) || string.IsNullOrWhiteSpace(input.Description)) throw new ArgumentException("Kupac i opis narudžbe su obavezni.");
        var number = $"NAR-{DateTime.UtcNow:yyyy}-{orders.Count + 1:000}";
        var assignedTo = "Automatska raspodjela";
        var workOrder = operationsStore.CreateWorkOrder(new CreateWorkOrderInput { Title = $"Narudžba {number}: {input.CustomerName.Trim()}", Description = input.Description, Priority = input.Priority, AssignedTo = assignedTo, DueAt = input.RequiredBy }, actor);
        var order = new CustomerOrder(Guid.NewGuid(), number, input.CustomerName.Trim(), input.CustomerReference?.Trim(), input.Description.Trim(), input.Priority, input.RequiredBy, "work_order_created", assignedTo, workOrder.Number, DateTimeOffset.UtcNow);
        orders.Add(order); return order;
    }
}
