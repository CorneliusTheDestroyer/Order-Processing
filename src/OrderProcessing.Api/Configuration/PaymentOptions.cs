namespace OrderProcessing.Api.Configuration;

/// <summary>
/// Bound from the "Payment" section of appsettings.json (with an appsettings.Development.json
/// override) via the Options pattern, so the simulated gateway's behaviour is configuration-driven
/// instead of a hardcoded constant in Program.cs.
/// </summary>
public class PaymentOptions
{
    /// <summary>Probability (0.0–1.0) that a simulated payment attempt is declined.</summary>
    public double FailureRate { get; set; } = 0.1;
}
