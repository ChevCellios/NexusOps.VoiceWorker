namespace NexusOps.Web.Models;
public sealed record InventoryStockItem(string Warehouse,string Code,string Name,string Unit,decimal Quantity,decimal Reserved,decimal Minimum,decimal UnitCost){public decimal Available=>Quantity-Reserved;public decimal Value=>Quantity*UnitCost;}
