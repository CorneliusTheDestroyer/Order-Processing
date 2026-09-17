namespace OrderProcessing.Api.Services;

/// <summary>
/// Approves payments randomly at a configurable failure rate, so the "payment processing failures"
/// error scenario is actually reachable during manual testing instead of only in unit tests.
/// The failure rate is a constructor parameter, bound from configuration (PaymentOptions.FailureRate)
/// by the DI registration in Program.cs, so it can be tuned per environment without a code change.
/// </summary>
public class RandomPaymentGatewaySimulator : IPaymentGatewaySimulator
{
    private readonly double _failureRate;

    public RandomPaymentGatewaySimulator(double failureRate = 0.1)
    {
        if (failureRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failureRate), failureRate, "Failure rate must be between 0 and 1.");
        }

        _failureRate = failureRate;
    }

    public bool AuthorizePayment(decimal amount) => Random.Shared.NextDouble() >= _failureRate;
}
