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
}
