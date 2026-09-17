using Microsoft.EntityFrameworkCore;
using OrderProcessing.Api.Clients;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _context;
    private readonly IInventoryClient _inventoryClient;
    private readonly IPaymentClient _paymentClient;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        AppDbContext context, IInventoryClient inventoryClient, IPaymentClient paymentClient, ILogger<OrderService> logger)
    {
        _context = context;
        _inventoryClient = inventoryClient;
        _paymentClient = paymentClient;
        _logger = logger;
    }

    public async Task<OperationResult<Order>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Validate order data and items. [ApiController] model validation already rejected
        // missing/invalid fields before this method runs; this covers the cross-field rule
        // attributes can't express.
        var duplicateProductIds = request.Items
            .GroupBy(i => i.ProductId, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1);

        if (duplicateProductIds)
        {
            return OperationResult<Order>.ValidationFailed(
                "Duplicate productId entries are not allowed in a single order; combine quantities into one line instead.");
        }

        var order = new Order
        {
            CustomerId = request.CustomerId,
            Status = OrderStatus.Pending,
            Items = request.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.Quantity * i.UnitPrice);

        // 2 & 3. Check availability and reserve inventory for every item, over HTTP, one product at
        // a time (the assessment doesn't define a bulk-reserve endpoint). If any item can't be
        // reserved, roll back everything already reserved for this order — a partially-reservable
        // order should never hold inventory hostage.
        var reserved = new List<(string ProductId, int Quantity)>();

        foreach (var item in order.Items)
        {
            var reserveResult = await _inventoryClient.ReserveAsync(item.ProductId, item.Quantity, cancellationToken);

            if (!reserveResult.IsSuccess)
            {
                await ReleaseAllAsync(reserved, cancellationToken);

                return reserveResult.Outcome switch
                {
                    OperationOutcome.NotFound => OperationResult<Order>.ValidationFailed(
                        $"Product '{item.ProductId}' does not exist."),
                    OperationOutcome.Conflict => OperationResult<Order>.Conflict(
                        reserveResult.Error ?? $"Insufficient inventory for '{item.ProductId}'."),
                    OperationOutcome.Unavailable => OperationResult<Order>.Unavailable(
                        reserveResult.Error ?? "The inventory service is unavailable."),
                    _ => OperationResult<Order>.ValidationFailed(
                        reserveResult.Error ?? $"Could not reserve '{item.ProductId}'.")
                };
            }

            reserved.Add((item.ProductId, item.Quantity));
        }

        // Persist the order (Pending) now that inventory is secured, so a payment failure below has
        // a real, retrievable order row to attach the outcome to.
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        // 4. Process payment.
        var paymentResult = await _paymentClient.ProcessAsync(order.Id, order.TotalAmount, cancellationToken);

        if (!paymentResult.IsSuccess)
        {
            // The payment service itself couldn't be reached/errored — as opposed to reachable but
            // declining the payment, handled below. Release inventory either way and surface this
            // distinctly so the caller can tell "try again later" apart from "your payment was
            // declined".
            await ReleaseAllAsync(reserved, cancellationToken);

            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                "Order {OrderId} cancelled: payment service call failed ({Error}).", order.Id, paymentResult.Error);

            return paymentResult.Outcome == OperationOutcome.Unavailable
                ? OperationResult<Order>.Unavailable(paymentResult.Error ?? "The payment service is unavailable.")
                : OperationResult<Order>.Conflict(paymentResult.Error ?? "Payment could not be processed.");
        }

        // 5 & 6. Confirm on success; release inventory and cancel on a decline.
        if (paymentResult.Value!.Status == PaymentStatus.Completed)
        {
            order.Status = OrderStatus.Confirmed;
            _logger.LogInformation("Order {OrderId} confirmed after successful payment.", order.Id);
        }
        else
        {
            await ReleaseAllAsync(reserved, cancellationToken);
            order.Status = OrderStatus.Cancelled;
            _logger.LogWarning("Order {OrderId} cancelled: payment was declined; inventory released.", order.Id);
        }

        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        // Whether confirmed or cancelled, the order was carried all the way through the flow and is
        // a real, retrievable resource — the caller inspects Status to see which happened, the same
        // way PaymentService treats a decline as a successfully recorded transaction rather than an
        // error.
        return OperationResult<Order>.Success(order);
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<PagedResult<Order>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Order>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<OperationResult<Order>> UpdateStatusAsync(
        Guid id, OrderStatus newStatus, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return OperationResult<Order>.NotFound($"Order '{id}' was not found.");
        }

        if (!IsValidTransition(order.Status, newStatus))
        {
            return OperationResult<Order>.Conflict(
                $"Cannot change order status from '{order.Status}' to '{newStatus}'.");
        }

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<Order>.Success(order);
    }

    /// <summary>A deliberately simple state machine: Pending -> Confirmed/Cancelled,
    /// Confirmed -> Shipped/Cancelled; Cancelled and Shipped are terminal. Setting a status equal
    /// to the current one is treated as a harmless no-op rather than an error.</summary>
    private static bool IsValidTransition(OrderStatus current, OrderStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            OrderStatus.Pending => next is OrderStatus.Confirmed or OrderStatus.Cancelled,
            OrderStatus.Confirmed => next is OrderStatus.Shipped or OrderStatus.Cancelled,
            OrderStatus.Cancelled => false,
            OrderStatus.Shipped => false,
            _ => false
        };
    }

    /// <summary>Best-effort compensation for a partially-reserved order. Logs failures loudly but
    /// doesn't let them override the caller's original error — a stuck reservation here is an
    /// alerting concern, not something that should mask the reason the order failed in the first
    /// place.</summary>
    private async Task ReleaseAllAsync(List<(string ProductId, int Quantity)> reserved, CancellationToken cancellationToken)
    {
        foreach (var (productId, quantity) in reserved)
        {
            var release = await _inventoryClient.ReleaseAsync(productId, quantity, cancellationToken);

            if (!release.IsSuccess)
            {
                _logger.LogError(
                    "Failed to release {Quantity} units of {ProductId} during rollback: {Error}",
                    quantity, productId, release.Error);
            }
        }
    }
}
