namespace NexusOps.Web.Models;

public enum WorkOrderStatus { New, Assigned, InProgress, WaitingParts, Completed }
public enum WorkOrderPriority { Low, Normal, High, Critical }

public sealed record Asset(Guid Id, string Name, string Code, string Location, string Status);

public sealed class CreateAssetInput
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
}

public sealed record WorkOrder(
    Guid Id,
    string Number,
    string Title,
    Guid AssetId,
    WorkOrderPriority Priority,
    WorkOrderStatus Status,
    string AssignedTo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueAt,
    string? Description);

public sealed record DashboardSummary(int OpenWorkOrders, int CriticalWorkOrders, int AssetsNeedingAttention, int CompletedThisMonth);

public sealed class CreateWorkOrderInput
{
    public string Title { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public WorkOrderPriority Priority { get; init; } = WorkOrderPriority.Normal;
    public string AssignedTo { get; init; } = string.Empty;
    public string? Description { get; init; }
}
