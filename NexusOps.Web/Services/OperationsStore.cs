using NexusOps.Web.Models;

namespace NexusOps.Web.Services;

public interface IOperationsStore
{
    IReadOnlyList<Asset> ListAssets();
    Asset CreateAsset(CreateAssetInput input);
    IReadOnlyList<WorkOrder> ListWorkOrders();
    WorkOrder? GetWorkOrder(Guid id);
    IReadOnlyList<WorkOrderEvent> ListWorkOrderEvents(Guid workOrderId);
    void UpdateWorkOrderStatus(Guid id, WorkOrderStatus status, string? actorName);
    DashboardSummary GetSummary();
    WorkOrder CreateWorkOrder(CreateWorkOrderInput input);
}

public sealed class InMemoryOperationsStore : IOperationsStore
{
    private readonly List<Asset> _assets =
    [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "CNC glodalica 01", "CNC-01", "Pogon A", "Operativan"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000002"), "Kompresor 02", "CMP-02", "Pogon B", "Potrebna provjera"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000003"), "Transportna traka 03", "TRK-03", "Linija 3", "Operativan")
    ];
    private readonly List<WorkOrder> _workOrders;
    private readonly List<WorkOrderEvent> _workOrderEvents = [];

    public InMemoryOperationsStore()
    {
        _workOrders =
        [
            new(Guid.NewGuid(), "RN-2026-001", "Provjera tlaka kompresora", _assets[1].Id, WorkOrderPriority.High, WorkOrderStatus.Assigned, "Marko Horvat", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "Očitati tlak i provjeriti filtre."),
            new(Guid.NewGuid(), "RN-2026-002", "Redovni servis CNC glodalice", _assets[0].Id, WorkOrderPriority.Normal, WorkOrderStatus.InProgress, "Ana Kovač", DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(3), "Mjesečni preventivni servis."),
            new(Guid.NewGuid(), "RN-2026-003", "Zamjena senzora trake", _assets[2].Id, WorkOrderPriority.Critical, WorkOrderStatus.New, "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8), "Senzor povremeno gubi signal.")
        ];
    }

    public IReadOnlyList<Asset> ListAssets() => _assets;
    public Asset CreateAsset(CreateAssetInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Code)) throw new ArgumentException("Naziv i šifra stroja su obavezni.");
        if (_assets.Any(asset => string.Equals(asset.Code, input.Code.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Stroj s tom šifrom već postoji.");
        var asset = new Asset(Guid.NewGuid(), input.Name.Trim(), input.Code.Trim(), input.Location.Trim(), "Operativan");
        _assets.Add(asset);
        return asset;
    }
    public IReadOnlyList<WorkOrder> ListWorkOrders() => _workOrders.OrderByDescending(order => order.CreatedAt).ToArray();
    public WorkOrder? GetWorkOrder(Guid id) => _workOrders.FirstOrDefault(order => order.Id == id);
    public IReadOnlyList<WorkOrderEvent> ListWorkOrderEvents(Guid workOrderId) =>
        _workOrderEvents.Where(item => item.WorkOrderId == workOrderId).OrderByDescending(item => item.CreatedAt).ToArray();
    public void UpdateWorkOrderStatus(Guid id, WorkOrderStatus status, string? actorName)
    {
        var order = GetWorkOrder(id) ?? throw new KeyNotFoundException("Radni nalog nije pronađen.");
        var index = _workOrders.IndexOf(order);
        _workOrders[index] = order with { Status = status };
        _workOrderEvents.Add(new WorkOrderEvent(Guid.NewGuid(), order.Id, "status_changed", $"Status promijenjen u {status}.", actorName, DateTimeOffset.UtcNow));
    }
    public DashboardSummary GetSummary() => new(
        _workOrders.Count(order => order.Status is not WorkOrderStatus.Completed),
        _workOrders.Count(order => order.Priority is WorkOrderPriority.Critical && order.Status is not WorkOrderStatus.Completed),
        _assets.Count(asset => asset.Status != "Operativan"),
        _workOrders.Count(order => order.Status is WorkOrderStatus.Completed && order.CreatedAt.Month == DateTime.UtcNow.Month));

    public WorkOrder CreateWorkOrder(CreateWorkOrderInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) throw new ArgumentException("Naslov radnog naloga je obavezan.");
        if (!_assets.Any(asset => asset.Id == input.AssetId)) throw new ArgumentException("Odabrani stroj ne postoji.");
        var workOrder = new WorkOrder(Guid.NewGuid(), $"RN-{DateTime.UtcNow:yyyy}-{_workOrders.Count + 1:000}", input.Title.Trim(), input.AssetId, input.Priority, WorkOrderStatus.New, input.AssignedTo.Trim(), DateTimeOffset.UtcNow, input.DueAt, input.Description?.Trim());
        _workOrders.Add(workOrder);
        _workOrderEvents.Add(new WorkOrderEvent(Guid.NewGuid(), workOrder.Id, "created", "Radni nalog je otvoren putem web aplikacije.", "Administrator", DateTimeOffset.UtcNow));
        return workOrder;
    }
}
