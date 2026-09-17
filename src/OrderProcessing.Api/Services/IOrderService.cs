using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Services;

public interface IOrderService
{
    /// <summary>Runs the full order-creation flow: validate, reserve inventory, take payment,
    /// confirm or roll back. A Success result can still carry an order with Status == Cancelled
    /// (payment declined) — that's a completed, valid operation, not an error. Non-Success
    /// outcomes mean the order was never created at all (bad input, a product that doesn't exist,
    /// or a downstream service that couldn't be reached).</summary>
    Task<OperationResult<Order>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<Order>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<OperationResult<Order>> UpdateStatusAsync(Guid id, OrderStatus newStatus, CancellationToken cancellationToken = default);
}
