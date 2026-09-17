using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Api.Clients;

/// <summary>OrderService's view of the Payment Controller, over real loopback HTTP (see
/// IInventoryClient for why).</summary>
public interface IPaymentClient
{
    Task<OperationResult<PaymentTransactionResponse>> ProcessAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default);
}
