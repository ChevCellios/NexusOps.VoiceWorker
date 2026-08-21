using System.ComponentModel.DataAnnotations;

namespace NexusOps.Web.Models;

public enum WorkOrderStatus
{
    [Display(Name = "Novi")] New,
    [Display(Name = "Dodijeljen")] Assigned,
    [Display(Name = "U tijeku")] InProgress,
    [Display(Name = "Čeka dijelove")] WaitingParts,
    [Display(Name = "Završen")] Completed
}

public enum WorkOrderPriority
{
    [Display(Name = "Nizak")] Low,
    [Display(Name = "Normalan")] Normal,
    [Display(Name = "Visok")] High,
    [Display(Name = "Hitno")] Critical
}

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

public sealed record WorkOrderEvent(
    Guid Id,
    Guid WorkOrderId,
    string EventType,
    string? Message,
    string? ActorName,
    DateTimeOffset CreatedAt);

public sealed record DashboardSummary(int OpenWorkOrders, int CriticalWorkOrders, int AssetsNeedingAttention, int CompletedThisMonth);

public sealed class CreateWorkOrderInput
{
    [Required(ErrorMessage = "Unesi naziv radnog naloga.")]
    [StringLength(250, ErrorMessage = "Naziv može imati najviše 250 znakova.")]
    public string Title { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public WorkOrderPriority Priority { get; init; } = WorkOrderPriority.Normal;
    [StringLength(150, ErrorMessage = "Dodijeljena osoba može imati najviše 150 znakova.")]
    public string AssignedTo { get; init; } = string.Empty;
    [DataType(DataType.DateTime)]
    public DateTimeOffset? DueAt { get; init; }
    [StringLength(4000, ErrorMessage = "Opis može imati najviše 4.000 znakova.")]
    public string? Description { get; init; }
}
