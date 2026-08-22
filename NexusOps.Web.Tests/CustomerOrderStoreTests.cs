using NexusOps.Web.Models;
using NexusOps.Web.Services;
using Xunit;

namespace NexusOps.Web.Tests;

public sealed class CustomerOrderStoreTests
{
    [Fact]
    public void Receive_CreatesCustomerOrderAndWorkOrder()
    {
        var operations = new InMemoryOperationsStore();
        var store = new InMemoryCustomerOrderStore(operations);

        var order = store.Receive(new CreateCustomerOrderInput
        {
            CustomerName = "Adria kupac d.o.o.",
            Description = "Pripremiti i isporučiti automatizacijsku ćeliju.",
            Priority = WorkOrderPriority.High
        }, "administrator@nexusops.hr");

        Assert.Equal("work_order_created", order.Status);
        Assert.NotEmpty(order.WorkOrderNumber);
        Assert.Contains(operations.ListWorkOrders(), item => item.Number == order.WorkOrderNumber);
    }
}
