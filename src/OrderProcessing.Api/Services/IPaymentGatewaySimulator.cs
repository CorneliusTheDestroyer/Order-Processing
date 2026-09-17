namespace OrderProcessing.Api.Services;

/// <summary>
/// Stands in for a real external payment gateway (there's obviously no real one to call for this
/// assessment). Isolated behind an interface for two reasons: PaymentService shouldn't care how
/// authorization decisions are made, and Task 8's unit tests need to force a specific
/// approve/decline outcome deterministically rather than depending on randomness.
/// </summary>
public interface IPaymentGatewaySimulator
{
    bool AuthorizePayment(decimal amount);
}
