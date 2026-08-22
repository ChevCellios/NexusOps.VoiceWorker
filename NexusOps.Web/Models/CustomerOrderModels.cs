using System.ComponentModel.DataAnnotations;

namespace NexusOps.Web.Models;

public sealed record CustomerOrder(
    Guid Id,
    string Number,
    string CustomerName,
    string? CustomerReference,
    string Description,
    WorkOrderPriority Priority,
    DateTimeOffset? RequiredBy,
    string Status,
    string AssignedTo,
    string WorkOrderNumber,
    DateTimeOffset CreatedAt);

public sealed class CreateCustomerOrderInput
{
    [Required(ErrorMessage = "Unesi naziv kupca.")]
    [StringLength(200)]
    public string CustomerName { get; set; } = string.Empty;

    [StringLength(120)]
    public string? CustomerReference { get; set; }

    [Required(ErrorMessage = "Unesi što narudžba obuhvaća.")]
    [StringLength(4000)]
    public string Description { get; set; } = string.Empty;

    public WorkOrderPriority Priority { get; set; } = WorkOrderPriority.Normal;
    public DateTimeOffset? RequiredBy { get; set; }
}
