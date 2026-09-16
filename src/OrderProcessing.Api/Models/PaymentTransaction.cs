namespace OrderProcessing.Api.Models;

/// <summary>
/// A payment attempt against an order, as defined by the assessment's "Payment Transaction" data
/// model.
/// </summary>
public class PaymentTransaction
{
    public Guid TransactionId { get; set; } = Guid.NewGuid();

    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
