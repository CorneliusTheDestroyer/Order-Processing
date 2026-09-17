using Microsoft.EntityFrameworkCore;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly IPaymentGatewaySimulator _gateway;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(AppDbContext context, IPaymentGatewaySimulator gateway, ILogger<PaymentService> logger)
    {
        _context = context;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<OperationResult<PaymentTransaction>> ProcessAsync(
        Guid orderId, decimal amount, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            return OperationResult<PaymentTransaction>.ValidationFailed("OrderId is required.");
        }

        if (amount <= 0)
        {
            return OperationResult<PaymentTransaction>.ValidationFailed("Amount must be greater than zero.");
        }

        var approved = _gateway.AuthorizePayment(amount);

        var transaction = new PaymentTransaction
        {
            OrderId = orderId,
            Amount = amount,
            Status = approved ? PaymentStatus.Completed : PaymentStatus.Failed,
            ProcessedAt = DateTime.UtcNow
        };

        _context.PaymentTransactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        if (approved)
        {
            _logger.LogInformation(
                "Payment {TransactionId} for order {OrderId} completed for {Amount:C}.",
                transaction.TransactionId, orderId, amount);
        }
        else
        {
            _logger.LogWarning(
                "Payment {TransactionId} for order {OrderId} was declined for {Amount:C}.",
                transaction.TransactionId, orderId, amount);
        }

        // A declined payment is still a successfully *processed* request — we recorded a
        // transaction with Status = Failed. The caller (OrderService, Task 5) inspects that status
        // to decide whether to confirm the order or release its inventory reservation.
        return OperationResult<PaymentTransaction>.Success(transaction);
    }

    public async Task<PaymentTransaction?> GetAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        return await _context.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TransactionId == transactionId, cancellationToken);
    }
}
