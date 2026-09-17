using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Api.Clients;

/// <summary>
/// OrderService's view of the Inventory Controller. This is a genuine HTTP call (loopback, to this
/// same running process) rather than an in-process method call — the assessment's "HTTP Client for
/// inter-service communication" requirement, and its "service unavailability" error scenario, only
/// mean something if Order and Inventory actually talk over the network rather than sharing a
/// method call stack.
/// </summary>
public interface IInventoryClient
{
    Task<OperationResult<InventoryItemResponse>> ReserveAsync(string productId, int quantity, CancellationToken cancellationToken = default);

    Task<OperationResult<InventoryItemResponse>> ReleaseAsync(string productId, int quantity, CancellationToken cancellationToken = default);
}
