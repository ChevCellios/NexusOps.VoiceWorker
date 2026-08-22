using NexusOps.Web.Models;
using NexusOps.Web.Services;
using Xunit;

namespace NexusOps.Web.Tests;

public sealed class InventoryStoreTests
{
    [Fact]
    public void RecordReceipt_IncreasesStock()
    {
        var store = new InMemoryInventoryStore();
        var item = store.ListStock().First();

        store.RecordMovement(new InventoryMovementInput { StockId = item.StockId, MovementType = "receipt", Quantity = 5 }, "tester");

        Assert.Equal(item.Quantity + 5, store.ListStock().First(x => x.StockId == item.StockId).Quantity);
    }

    [Fact]
    public void RecordIssue_RejectsQuantityBeyondStock()
    {
        var store = new InMemoryInventoryStore();
        var item = store.ListStock().First();

        Assert.Throws<ArgumentException>(() => store.RecordMovement(new InventoryMovementInput { StockId = item.StockId, MovementType = "issue", Quantity = item.Quantity + 1 }, "tester"));
    }

    [Fact]
    public void Transfer_MovesAvailableStockBetweenWarehouses()
    {
        var store = new InMemoryInventoryStore();
        var source = store.ListStock().First();
        var destination = store.ListWarehouses().Single(x => x.Id != source.WarehouseId);

        store.Transfer(new InventoryTransferInput { SourceStockId = source.StockId, DestinationWarehouseId = destination.Id, Quantity = 3 }, "tester");

        Assert.Equal(source.Quantity - 3, store.ListStock().Single(x => x.StockId == source.StockId).Quantity);
        Assert.Contains(store.ListStock(), x => x.WarehouseId == destination.Id && x.Code == source.Code && x.Quantity == 3);
    }

    [Fact]
    public void RecordIssue_WithWorkOrderTracksMaterialCost()
    {
        var store = new InMemoryInventoryStore();
        var item = store.ListStock().First();
        var workOrderId = Guid.NewGuid();

        store.RecordMovement(new InventoryMovementInput { StockId = item.StockId, MovementType = "issue", Quantity = 2, WorkOrderId = workOrderId }, "tester");

        var usage = Assert.Single(store.ListWorkOrderMaterialUsage(workOrderId));
        Assert.Equal(item.Code, usage.Code);
        Assert.Equal(2 * item.UnitCost, usage.Cost);
    }
}
