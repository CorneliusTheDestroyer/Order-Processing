namespace OrderProcessing.Api.Models;

/// <summary>
/// Lifecycle states for a <see cref="PaymentTransaction"/>, matching the assessment's data model
/// ("pending|completed|failed").
/// </summary>
public enum PaymentStatus
{
    Pending,
    Completed,
    Failed
}
