using System.ComponentModel.DataAnnotations;

namespace NexusOps.Web.Models;

public sealed record InventoryStockItem(
    Guid StockId,
    Guid WarehouseId,
    string Warehouse,
    string Code,
    string Name,
    string Unit,
    decimal Quantity,
    decimal Reserved,
    decimal Minimum,
    decimal UnitCost)
{
    public decimal Available => Quantity - Reserved;
    public decimal Value => Quantity * UnitCost;
}

public sealed record InventoryWarehouse(Guid Id, string Name);

public sealed class InventoryMovementInput
{
    [Required(ErrorMessage = "Odaberi artikl u skladištu.")]
    public Guid StockId { get; set; }

    [Required(ErrorMessage = "Odaberi vrstu promjene.")]
    public string MovementType { get; set; } = "receipt";

    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "Količina mora biti veća od nule.")]
    public decimal Quantity { get; set; }

    public Guid? WorkOrderId { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

public sealed record WorkOrderMaterialUsage(string Code, string Name, string Unit, decimal Quantity, decimal UnitCost)
{
    public decimal Cost => Quantity * UnitCost;
}

public sealed class InventoryTransferInput
{
    [Required(ErrorMessage = "Odaberi artikl za prijenos.")]
    public Guid SourceStockId { get; set; }

    [Required(ErrorMessage = "Odaberi odredišno skladište.")]
    public Guid DestinationWarehouseId { get; set; }

    [Range(typeof(decimal), "0.001", "999999999", ErrorMessage = "Količina mora biti veća od nule.")]
    public decimal Quantity { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
