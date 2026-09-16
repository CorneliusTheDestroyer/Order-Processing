using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Data;

/// <summary>
/// Seeds a handful of products into the In-Memory inventory store so the API is exercisable
/// immediately after startup, without a separate setup step. Not meant to represent a real
/// catalog import — just enough fixture data to drive the order flow end-to-end.
/// </summary>
public static class DbSeeder
{
    public static void Seed(AppDbContext context)
    {
        if (context.InventoryItems.Any())
        {
            // Already seeded (e.g. a second scope resolved during startup) — don't duplicate.
            return;
        }

        context.InventoryItems.AddRange(
            new InventoryItem { ProductId = "SKU-001", AvailableQuantity = 50, ReservedQuantity = 0 },
            new InventoryItem { ProductId = "SKU-002", AvailableQuantity = 25, ReservedQuantity = 0 },
            new InventoryItem { ProductId = "SKU-003", AvailableQuantity = 100, ReservedQuantity = 0 },
            new InventoryItem { ProductId = "SKU-004", AvailableQuantity = 5, ReservedQuantity = 0 },
            new InventoryItem { ProductId = "SKU-005", AvailableQuantity = 0, ReservedQuantity = 0 }
        );

        context.SaveChanges();
    }
}
