using OrderProcessing.Api.Common;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Services;

public interface IPaymentService
{
    /// <summary>Processes a payment attempt for an order. Note: a *declined* payment is still a
    /// successful result (Outcome.Success, with Value.Status == PaymentStatus.Failed) — the
    /// transaction was recorded correctly, it just wasn't approved. Outcome.ValidationFailed is
    /// reserved for malformed input (missing order id, non-positive amount).</summary>
    Task<OperationResult<PaymentTransaction>> ProcessAsync(Guid orderId, decimal amount, CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetAsync(Guid transactionId, CancellationToken cancellationToken = default);
}
