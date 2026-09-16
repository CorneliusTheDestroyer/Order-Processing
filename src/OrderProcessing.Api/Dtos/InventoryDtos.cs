using System.ComponentModel.DataAnnotations;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Dtos;

/// <summary>Request body for reserving or releasing a quantity of a product.</summary>
public class InventoryQuantityRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; }
}

/// <summary>
/// Public shape of an inventory item. Deliberately omits RowVersion — that's an internal
/// persistence/concurrency detail, not part of the assessment's documented "Inventory Item" model.
/// </summary>
public class InventoryItemResponse
{
    public string ProductId { get; set; } = string.Empty;

    public int AvailableQuantity { get; set; }

    public int ReservedQuantity { get; set; }

    public static InventoryItemResponse FromModel(InventoryItem item) => new()
    {
        ProductId = item.ProductId,
        AvailableQuantity = item.AvailableQuantity,
        ReservedQuantity = item.ReservedQuantity
    };
}
