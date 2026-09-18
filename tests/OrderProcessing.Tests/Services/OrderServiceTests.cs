using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Api.Clients;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Models;
using OrderProcessing.Api.Services;
using OrderProcessing.Tests.TestHelpers;

namespace OrderProcessing.Tests.Services;

public class OrderServiceTests
{
    private static CreateOrderRequest SingleItemRequest(string productId = "SKU-001", int quantity = 2, decimal unitPrice = 10m) => new()
    {
        CustomerId = "cust-1",
        Items = new List<CreateOrderItemRequest>
        {
            new() { ProductId = productId, Quantity = quantity, UnitPrice = unitPrice }
        }
    };

    private static InventoryItemResponse InventoryResponse(string productId, int available = 8, int reserved = 2) => new()
    {
        ProductId = productId,
        AvailableQuantity = available,
        ReservedQuantity = reserved
    };

    [Fact]
    public async Task CreateAsync_ReturnsValidationFailed_WhenOrderHasDuplicateProductIds()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        var paymentClient = new Mock<IPaymentClient>();
        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var request = new CreateOrderRequest
        {
            CustomerId = "cust-1",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = "SKU-001", Quantity = 1, UnitPrice = 5m },
                new() { ProductId = "SKU-001", Quantity = 2, UnitPrice = 5m }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(OperationOutcome.ValidationFailed, result.Outcome);
        inventoryClient.Verify(c => c.ReserveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ConfirmsOrder_WhenInventoryReservedAndPaymentApproved()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        inventoryClient
            .Setup(c => c.ReserveAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001")));

        var paymentClient = new Mock<IPaymentClient>();
        paymentClient
            .Setup(c => c.ProcessAsync(It.IsAny<Guid>(), 20m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PaymentTransactionResponse>.Success(new PaymentTransactionResponse
            {
                TransactionId = Guid.NewGuid(),
                Amount = 20m,
                Status = PaymentStatus.Completed
            }));

        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var result = await service.CreateAsync(SingleItemRequest());

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.Confirmed, result.Value!.Status);
        inventoryClient.Verify(c => c.ReleaseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_CancelsOrderAsSuccess_AndReleasesInventory_WhenPaymentDeclined()
    {
        // A declined payment still results in Outcome.Success — the order was fully and correctly
        // processed, it just ended up Cancelled. This mirrors PaymentService's own
        // decline-is-still-success design.
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        inventoryClient
            .Setup(c => c.ReserveAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001")));
        inventoryClient
            .Setup(c => c.ReleaseAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001", available: 10, reserved: 0)));

        var paymentClient = new Mock<IPaymentClient>();
        paymentClient
            .Setup(c => c.ProcessAsync(It.IsAny<Guid>(), 20m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PaymentTransactionResponse>.Success(new PaymentTransactionResponse
            {
                TransactionId = Guid.NewGuid(),
                Amount = 20m,
                Status = PaymentStatus.Failed
            }));

        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var result = await service.CreateAsync(SingleItemRequest());

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.Cancelled, result.Value!.Status);
        inventoryClient.Verify(c => c.ReleaseAsync("SKU-001", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ReturnsConflict_AndRollsBackAlreadyReservedItems_WhenALaterItemHasInsufficientStock()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        inventoryClient
            .Setup(c => c.ReserveAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001")));
        inventoryClient
            .Setup(c => c.ReserveAsync("SKU-004", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Conflict(
                "Insufficient stock for 'SKU-004': requested 10, only 5 available."));
        inventoryClient
            .Setup(c => c.ReleaseAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001", available: 10, reserved: 0)));

        var paymentClient = new Mock<IPaymentClient>();

        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var request = new CreateOrderRequest
        {
            CustomerId = "cust-1",
            Items = new List<CreateOrderItemRequest>
            {
                new() { ProductId = "SKU-001", Quantity = 2, UnitPrice = 10m },
                new() { ProductId = "SKU-004", Quantity = 10, UnitPrice = 5m }
            }
        };

        var result = await service.CreateAsync(request);

        Assert.Equal(OperationOutcome.Conflict, result.Outcome);
        inventoryClient.Verify(c => c.ReleaseAsync("SKU-001", 2, It.IsAny<CancellationToken>()), Times.Once);
        paymentClient.Verify(c => c.ProcessAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);

        // The order should never have been persisted at all — reservation failed before it was saved.
        Assert.Empty(await context.Orders.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ReturnsValidationFailed_WhenProductDoesNotExist()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        inventoryClient
            .Setup(c => c.ReserveAsync("NOPE", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.NotFound("Product 'NOPE' was not found in inventory."));

        var paymentClient = new Mock<IPaymentClient>();
        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var result = await service.CreateAsync(SingleItemRequest(productId: "NOPE", quantity: 1));

        Assert.Equal(OperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_ReturnsUnavailable_WhenInventoryServiceUnreachable()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        inventoryClient
            .Setup(c => c.ReserveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Unavailable("Inventory returned HTTP 503."));

        var paymentClient = new Mock<IPaymentClient>();
        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var result = await service.CreateAsync(SingleItemRequest());

        Assert.Equal(OperationOutcome.Unavailable, result.Outcome);
        paymentClient.Verify(c => c.ProcessAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ReturnsUnavailable_AndReleasesInventory_WhenPaymentServiceUnreachable()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var inventoryClient = new Mock<IInventoryClient>();
        inventoryClient
            .Setup(c => c.ReserveAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001")));
        inventoryClient
            .Setup(c => c.ReleaseAsync("SKU-001", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<InventoryItemResponse>.Success(InventoryResponse("SKU-001", available: 10, reserved: 0)));

        var paymentClient = new Mock<IPaymentClient>();
        paymentClient
            .Setup(c => c.ProcessAsync(It.IsAny<Guid>(), 20m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PaymentTransactionResponse>.Unavailable("Payment returned HTTP 503."));

        var service = new OrderService(context, inventoryClient.Object, paymentClient.Object, NullLogger<OrderService>.Instance);

        var result = await service.CreateAsync(SingleItemRequest());

        Assert.Equal(OperationOutcome.Unavailable, result.Outcome);
        inventoryClient.Verify(c => c.ReleaseAsync("SKU-001", 2, It.IsAny<CancellationToken>()), Times.Once);

        // The order was persisted before the payment attempt, so it should exist as a Cancelled
        // record of what was tried, rather than vanishing entirely.
        var order = Assert.Single(await context.Orders.ToListAsync());
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenOrderDoesNotExist()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var service = new OrderService(context, Mock.Of<IInventoryClient>(), Mock.Of<IPaymentClient>(), NullLogger<OrderService>.Instance);

        var result = await service.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsNotFound_WhenOrderDoesNotExist()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var service = new OrderService(context, Mock.Of<IInventoryClient>(), Mock.Of<IPaymentClient>(), NullLogger<OrderService>.Instance);

        var result = await service.UpdateStatusAsync(Guid.NewGuid(), OrderStatus.Confirmed);

        Assert.Equal(OperationOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task UpdateStatusAsync_AllowsValidTransition_FromPendingToConfirmed()
    {
        var dbName = Guid.NewGuid().ToString();
        var orderId = Guid.NewGuid();

        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.Orders.Add(new Order { Id = orderId, CustomerId = "cust-1", Status = OrderStatus.Pending });
            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = new OrderService(context, Mock.Of<IInventoryClient>(), Mock.Of<IPaymentClient>(), NullLogger<OrderService>.Instance);

        var result = await service.UpdateStatusAsync(orderId, OrderStatus.Confirmed);

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(OrderStatus.Confirmed, result.Value!.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_ReturnsConflict_WhenTransitioningOutOfATerminalStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var orderId = Guid.NewGuid();

        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.Orders.Add(new Order { Id = orderId, CustomerId = "cust-1", Status = OrderStatus.Shipped });
            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = new OrderService(context, Mock.Of<IInventoryClient>(), Mock.Of<IPaymentClient>(), NullLogger<OrderService>.Instance);

        var result = await service.UpdateStatusAsync(orderId, OrderStatus.Pending);

        Assert.Equal(OperationOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task ListAsync_ReturnsRequestedPage_OrderedByNewestFirst()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            for (var i = 0; i < 5; i++)
            {
                seedContext.Orders.Add(new Order
                {
                    CustomerId = $"cust-{i}",
                    CreatedAt = DateTime.UtcNow.AddMinutes(i)
                });
            }

            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = new OrderService(context, Mock.Of<IInventoryClient>(), Mock.Of<IPaymentClient>(), NullLogger<OrderService>.Instance);

        var page = await service.ListAsync(page: 1, pageSize: 2);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("cust-4", page.Items[0].CustomerId); // newest first
    }
}
