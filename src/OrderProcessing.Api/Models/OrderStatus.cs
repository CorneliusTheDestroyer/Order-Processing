namespace OrderProcessing.Api.Models;

/// <summary>
/// Lifecycle states for an <see cref="Order"/>, matching the assessment's data model
/// ("pending|confirmed|cancelled|shipped").
/// </summary>
public enum OrderStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Shipped
}
