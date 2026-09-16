using OrderProcessing.Api.Common;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Services;

public interface IInventoryService
{
    /// <summary>Reads current availability for a product, or null if it isn't tracked.</summary>
    Task<InventoryItem?> GetAsync(string productId, CancellationToken cancellationToken = default);

    /// <summary>Moves <paramref name="quantity"/> units from available to reserved stock.
    /// Fails with Conflict if there isn't enough available stock.</summary>
    Task<OperationResult<InventoryItem>> ReserveAsync(string productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Moves <paramref name="quantity"/> units back from reserved to available stock
    /// (e.g. because payment failed after inventory was reserved). Fails with Conflict if more is
    /// being released than is currently reserved.</summary>
    Task<OperationResult<InventoryItem>> ReleaseAsync(string productId, int quantity, CancellationToken cancellationToken = default);
}
