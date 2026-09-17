using System.ComponentModel.DataAnnotations;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Dtos;

public class CreateOrderItemRequest
{
    [Required(AllowEmptyStrings = false)]
    public string ProductId { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "UnitPrice must be greater than zero.")]
    public decimal UnitPrice { get; set; }
}

public class CreateOrderRequest
{
    [Required(AllowEmptyStrings = false)]
    public string CustomerId { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "An order must contain at least one item.")]
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; set; }
}

public class OrderItemResponse
{
    public string ProductId { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}

public class OrderResponse
{
    public Guid Id { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public List<OrderItemResponse> Items { get; set; } = new();

    public decimal TotalAmount { get; set; }

    public OrderStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public static OrderResponse FromModel(Order order) => new()
    {
        Id = order.Id,
        CustomerId = order.CustomerId,
        Items = order.Items.Select(i => new OrderItemResponse
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            LineTotal = i.LineTotal
        }).ToList(),
        TotalAmount = order.TotalAmount,
        Status = order.Status,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };
}
