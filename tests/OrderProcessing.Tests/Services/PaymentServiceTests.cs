using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Models;
using OrderProcessing.Api.Services;
using OrderProcessing.Tests.TestHelpers;

namespace OrderProcessing.Tests.Services;

public class PaymentServiceTests
{
    [Fact]
    public async Task ProcessAsync_ReturnsValidationFailed_WhenOrderIdIsEmpty()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var gateway = new Mock<IPaymentGatewaySimulator>();
        var service = new PaymentService(context, gateway.Object, NullLogger<PaymentService>.Instance);

        var result = await service.ProcessAsync(Guid.Empty, 10m);

        Assert.Equal(OperationOutcome.ValidationFailed, result.Outcome);
        gateway.Verify(g => g.AuthorizePayment(It.IsAny<decimal>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ProcessAsync_ReturnsValidationFailed_WhenAmountIsNotPositive(decimal amount)
    {
        await using var context = InMemoryDbContextFactory.Create();
        var gateway = new Mock<IPaymentGatewaySimulator>();
        var service = new PaymentService(context, gateway.Object, NullLogger<PaymentService>.Instance);

        var result = await service.ProcessAsync(Guid.NewGuid(), amount);

        Assert.Equal(OperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSuccessWithCompletedStatus_WhenGatewayApproves()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var gateway = new Mock<IPaymentGatewaySimulator>();
        gateway.Setup(g => g.AuthorizePayment(It.IsAny<decimal>())).Returns(true);
        var service = new PaymentService(context, gateway.Object, NullLogger<PaymentService>.Instance);

        var orderId = Guid.NewGuid();
        var result = await service.ProcessAsync(orderId, 49.99m);

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(PaymentStatus.Completed, result.Value!.Status);
        Assert.Equal(orderId, result.Value.OrderId);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSuccessWithFailedStatus_WhenGatewayDeclines()
    {
        // The key design decision this covers: a *declined* payment is still Outcome.Success — the
        // request was handled correctly, it just wasn't approved. OrderService relies on exactly
        // this distinction to tell "payment declined" apart from "payment service unreachable".
        await using var context = InMemoryDbContextFactory.Create();
        var gateway = new Mock<IPaymentGatewaySimulator>();
        gateway.Setup(g => g.AuthorizePayment(It.IsAny<decimal>())).Returns(false);
        var service = new PaymentService(context, gateway.Object, NullLogger<PaymentService>.Instance);

        var result = await service.ProcessAsync(Guid.NewGuid(), 49.99m);

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(PaymentStatus.Failed, result.Value!.Status);
    }

    [Fact]
    public async Task ProcessAsync_PersistsTransaction_RetrievableViaGetAsync()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var gateway = new Mock<IPaymentGatewaySimulator>();
        gateway.Setup(g => g.AuthorizePayment(It.IsAny<decimal>())).Returns(true);
        var service = new PaymentService(context, gateway.Object, NullLogger<PaymentService>.Instance);

        var result = await service.ProcessAsync(Guid.NewGuid(), 15m);
        var fetched = await service.GetAsync(result.Value!.TransactionId);

        Assert.NotNull(fetched);
        Assert.Equal(result.Value.TransactionId, fetched!.TransactionId);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTransactionDoesNotExist()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var gateway = new Mock<IPaymentGatewaySimulator>();
        var service = new PaymentService(context, gateway.Object, NullLogger<PaymentService>.Instance);

        var result = await service.GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
