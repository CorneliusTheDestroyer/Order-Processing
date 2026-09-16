namespace OrderProcessing.Api.Models;

/// <summary>
/// A single line item within an <see cref="Order"/>. Has its own surrogate key (Id) so EF Core
/// can track it as a dependent entity of Order, separate from the ProductId business key it refers
/// to in the Inventory domain.
/// </summary>
public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Foreign key back to the owning order.</summary>
    public Guid OrderId { get; set; }

    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>Convenience calculated value; not persisted.</summary>
    public decimal LineTotal => Quantity * UnitPrice;
}
