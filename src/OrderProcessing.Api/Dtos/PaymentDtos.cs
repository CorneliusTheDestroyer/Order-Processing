using System.ComponentModel.DataAnnotations;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Dtos;

/// <summary>Request body for processing a payment against an order.</summary>
public class ProcessPaymentRequest
{
    /// <remarks>[Required] is deliberately not used here — Guid is a non-nullable value type, so
    /// model binding always supplies *some* value (Guid.Empty if the field is omitted) and
    /// [Required] would never actually trigger. PaymentService checks for Guid.Empty explicitly
    /// instead.</remarks>
    public Guid OrderId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }
}

/// <summary>Public shape of a payment transaction, mirroring the assessment's documented model.</summary>
public class PaymentTransactionResponse
{
    public Guid TransactionId { get; set; }

    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    public PaymentStatus Status { get; set; }

    public DateTime ProcessedAt { get; set; }

    public static PaymentTransactionResponse FromModel(PaymentTransaction transaction) => new()
    {
        TransactionId = transaction.TransactionId,
        OrderId = transaction.OrderId,
        Amount = transaction.Amount,
        Status = transaction.Status,
        ProcessedAt = transaction.ProcessedAt
    };
}
