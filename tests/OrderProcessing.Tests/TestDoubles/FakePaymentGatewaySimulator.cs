using OrderProcessing.Api.Services;

namespace OrderProcessing.Tests.TestDoubles;

/// <summary>
/// Deterministic stand-in for RandomPaymentGatewaySimulator, used only by the end-to-end integration
/// tests so a payment's approval/decline outcome is a controlled precondition rather than a ~10-25%
/// chance the test flakes.
/// </summary>
public class FakePaymentGatewaySimulator : IPaymentGatewaySimulator
{
    private readonly bool _approve;

    public FakePaymentGatewaySimulator(bool approve)
    {
        _approve = approve;
    }

    public bool AuthorizePayment(decimal amount) => _approve;
}
