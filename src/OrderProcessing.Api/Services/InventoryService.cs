using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Services;

public class InventoryService : IInventoryService
{
    // The InMemory provider's RowVersion token means two concurrent reservations against the same
    // product will race: both read the same starting quantities, but only the first SaveChangesAsync
    // wins — the second gets a DbUpdateConcurrencyException. Retrying with a fresh read (instead of
    // failing outright) is what makes "concurrent order processing" actually work rather than just
    // fail unpredictably under load.
    private const int MaxConcurrencyRetries = 3;

    // Bonus: cache-aside on inventory reads. The TTL is a safety net only — every successful write
    // in MutateWithRetryAsync actively removes the entry, so callers never see stale availability
    // after a reserve/release; the TTL just bounds staleness if that invalidation were ever missed.
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(AppDbContext context, IMemoryCache cache, ILogger<InventoryService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<InventoryItem?> GetAsync(string productId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyFor(productId);

        if (_cache.TryGetValue<InventoryItem>(cacheKey, out var cached))
        {
            return cached;
        }

        var item = await _context.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);

        if (item is not null)
        {
            _cache.Set(cacheKey, item, CacheDuration);
        }

        return item;
    }

    private static string CacheKeyFor(string productId) => $"inventory:{productId}";

    public Task<OperationResult<InventoryItem>> ReserveAsync(string productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return Task.FromResult(OperationResult<InventoryItem>.ValidationFailed("Quantity must be greater than zero."));
        }

        return MutateWithRetryAsync(productId, isReserve: true, quantity, cancellationToken);
    }

    public Task<OperationResult<InventoryItem>> ReleaseAsync(string productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            return Task.FromResult(OperationResult<InventoryItem>.ValidationFailed("Quantity must be greater than zero."));
        }

        return MutateWithRetryAsync(productId, isReserve: false, quantity, cancellationToken);
    }

    private async Task<OperationResult<InventoryItem>> MutateWithRetryAsync(
        string productId, bool isReserve, int quantity, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var item = await _context.InventoryItems
                .FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);

            if (item is null)
            {
                return OperationResult<InventoryItem>.NotFound($"Product '{productId}' was not found in inventory.");
            }

            if (isReserve)
            {
                if (item.AvailableQuantity < quantity)
                {
                    return OperationResult<InventoryItem>.Conflict(
                        $"Insufficient stock for '{productId}': requested {quantity}, only {item.AvailableQuantity} available.");
                }

                item.AvailableQuantity -= quantity;
                item.ReservedQuantity += quantity;
            }
            else
            {
                if (item.ReservedQuantity < quantity)
                {
                    return OperationResult<InventoryItem>.Conflict(
                        $"Cannot release {quantity} units of '{productId}': only {item.ReservedQuantity} are currently reserved.");
                }

                item.ReservedQuantity -= quantity;
                item.AvailableQuantity += quantity;
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);

                // Actively invalidate rather than waiting out the TTL, so a GET immediately after a
                // reserve/release always reflects the write that just happened.
                _cache.Remove(CacheKeyFor(productId));

                return OperationResult<InventoryItem>.Success(item);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Drop this attempt's tracked copy so a retry re-reads the current
                // AvailableQuantity/ReservedQuantity/RowVersion from the store instead of retrying
                // against stale data.
                _context.Entry(item).State = EntityState.Detached;

                if (attempt == MaxConcurrencyRetries)
                {
                    _logger.LogError(
                        "Giving up on inventory update for {ProductId} after {MaxAttempts} concurrent-update retries.",
                        productId, MaxConcurrencyRetries);

                    return OperationResult<InventoryItem>.Conflict(
                        $"Could not update inventory for '{productId}' after {MaxConcurrencyRetries} attempts due to concurrent updates. Please retry.");
                }

                _logger.LogWarning(
                    "Concurrent inventory update detected for {ProductId} on attempt {Attempt}/{MaxAttempts}; retrying with a fresh read.",
                    productId, attempt, MaxConcurrencyRetries);
            }
        }

        // Unreachable: the loop above always returns on its final iteration (success, a business
        // conflict, not-found, or the concurrency give-up case). Present only to satisfy the
        // compiler's control-flow analysis for the async method's return type.
        throw new InvalidOperationException("Unreachable.");
    }
}
