namespace OrderProcessing.Api.Models;

/// <summary>
/// An e-commerce order, as defined by the assessment's "Order" data model. TotalAmount is
/// persisted (rather than always recomputed) because it reflects the price agreed at order time,
/// which should stay stable even if catalog prices change later.
/// </summary>
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CustomerId { get; set; } = string.Empty;

    public List<OrderItem> Items { get; set; } = new();

    public decimal TotalAmount { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
