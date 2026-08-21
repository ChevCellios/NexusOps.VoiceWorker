using NexusOps.Web.Models;
using NexusOps.Web.Services;
using Xunit;

namespace NexusOps.Web.Tests;

public sealed class OperationsStoreTests
{
    [Fact]
    public void CreateAsset_AddsAssetAndNormalizesItsValues()
    {
        var store = new InMemoryOperationsStore();

        var asset = store.CreateAsset(new CreateAssetInput
        {
            Name = "  Nova pumpa  ",
            Code = "  PMP-01  ",
            Location = "  Pogon C  "
        });

        Assert.Equal("Nova pumpa", asset.Name);
        Assert.Equal("PMP-01", asset.Code);
        Assert.Contains(store.ListAssets(), item => item.Id == asset.Id);
    }

    [Fact]
    public void CreateAsset_RejectsDuplicateCodeIgnoringLetterCase()
    {
        var store = new InMemoryOperationsStore();

        Assert.Throws<ArgumentException>(() => store.CreateAsset(new CreateAssetInput
        {
            Name = "Druga CNC glodalica",
            Code = "cnc-01",
            Location = "Pogon A"
        }));
    }

    [Fact]
    public void UpdateWorkOrderStatus_PersistsNewStatus()
    {
        var store = new InMemoryOperationsStore();
        var order = store.ListWorkOrders().First();

        store.UpdateWorkOrderStatus(order.Id, WorkOrderStatus.Completed, "Administrator");

        Assert.Equal(WorkOrderStatus.Completed, store.GetWorkOrder(order.Id)?.Status);
    }

    [Fact]
    public void CreateWorkOrder_RejectsUnknownAsset()
    {
        var store = new InMemoryOperationsStore();

        Assert.Throws<ArgumentException>(() => store.CreateWorkOrder(new CreateWorkOrderInput
        {
            Title = "Provjera nepostojećeg stroja",
            AssetId = Guid.NewGuid()
        }));
    }
}
