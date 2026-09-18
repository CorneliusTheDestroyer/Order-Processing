using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Models;
using OrderProcessing.Api.Services;
using OrderProcessing.Tests.TestHelpers;

namespace OrderProcessing.Tests.Services;

public class InventoryServiceTests
{
    private static InventoryService CreateService(AppDbContext context, IMemoryCache? cache = null) =>
        new(context, cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<InventoryService>.Instance);

    [Fact]
    public async Task GetAsync_ReturnsItem_WhenProductExists()
    {
        await using var context = InMemoryDbContextFactory.Create();
        context.InventoryItems.Add(new InventoryItem { ProductId = "SKU-100", AvailableQuantity = 10 });
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.GetAsync("SKU-100");

        Assert.NotNull(result);
        Assert.Equal(10, result!.AvailableQuantity);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenProductDoesNotExist()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var service = CreateService(context);

        var result = await service.GetAsync("does-not-exist");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ReserveAsync_ReturnsValidationFailed_WhenQuantityIsNotPositive(int quantity)
    {
        await using var context = InMemoryDbContextFactory.Create();
        var service = CreateService(context);

        var result = await service.ReserveAsync("SKU-100", quantity);

        Assert.Equal(OperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task ReserveAsync_ReturnsNotFound_WhenProductDoesNotExist()
    {
        await using var context = InMemoryDbContextFactory.Create();
        var service = CreateService(context);

        var result = await service.ReserveAsync("does-not-exist", 1);

        Assert.Equal(OperationOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task ReserveAsync_Succeeds_AndMovesStockFromAvailableToReserved_WhenSufficientStock()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = "SKU-100", AvailableQuantity = 10 });
            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = CreateService(context);

        var result = await service.ReserveAsync("SKU-100", 4);

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(6, result.Value!.AvailableQuantity);
        Assert.Equal(4, result.Value.ReservedQuantity);
    }

    [Fact]
    public async Task ReserveAsync_ReturnsConflict_WhenInsufficientStock()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = "SKU-004", AvailableQuantity = 5 });
            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = CreateService(context);

        var result = await service.ReserveAsync("SKU-004", 10);

        Assert.Equal(OperationOutcome.Conflict, result.Outcome);
        Assert.Contains("Insufficient stock", result.Error);
    }

    [Fact]
    public async Task ReleaseAsync_ReturnsConflict_WhenReleasingMoreThanReserved()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = "SKU-100", AvailableQuantity = 10, ReservedQuantity = 2 });
            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = CreateService(context);

        var result = await service.ReleaseAsync("SKU-100", 5);

        Assert.Equal(OperationOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task ReleaseAsync_Succeeds_AndMovesStockBackToAvailable()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = "SKU-100", AvailableQuantity = 6, ReservedQuantity = 4 });
            await seedContext.SaveChangesAsync();
        }

        await using var context = InMemoryDbContextFactory.Create(dbName);
        var service = CreateService(context);

        var result = await service.ReleaseAsync("SKU-100", 4);

        Assert.Equal(OperationOutcome.Success, result.Outcome);
        Assert.Equal(10, result.Value!.AvailableQuantity);
        Assert.Equal(0, result.Value.ReservedQuantity);
    }

    [Fact]
    public async Task GetAsync_ReflectsNewQuantity_AfterReserveAsyncInvalidatesTheCache()
    {
        var dbName = Guid.NewGuid().ToString();
        var cache = new MemoryCache(new MemoryCacheOptions());

        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = "SKU-100", AvailableQuantity = 10 });
            await seedContext.SaveChangesAsync();
        }

        // Same cache instance shared across three service instances, mimicking separate requests
        // within the same running app: a GET warms the cache, then a reserve should invalidate it
        // rather than leaving the GET's cached snapshot in place for the next read.
        await using var context1 = InMemoryDbContextFactory.Create(dbName);
        var readService = CreateService(context1, cache);
        var initial = await readService.GetAsync("SKU-100");
        Assert.Equal(10, initial!.AvailableQuantity);

        await using var context2 = InMemoryDbContextFactory.Create(dbName);
        var writeService = CreateService(context2, cache);
        await writeService.ReserveAsync("SKU-100", 3);

        await using var context3 = InMemoryDbContextFactory.Create(dbName);
        var verifyService = CreateService(context3, cache);
        var afterReserve = await verifyService.GetAsync("SKU-100");

        Assert.Equal(7, afterReserve!.AvailableQuantity);
    }

    [Fact]
    public async Task ReserveAsync_UnderConcurrentRequests_NeverOversellsStock()
    {
        // The core "concurrent order processing" edge case: fire more simultaneous reservation
        // requests than there is stock to cover, each against its own InventoryService/DbContext
        // (mirroring separate concurrent HTTP requests hitting the same store), and confirm the
        // optimistic-concurrency retry loop in InventoryService keeps the store consistent — exactly
        // as many succeed as there was stock for, the rest are rejected as Conflict, and the final
        // stock numbers reflect exactly the successful reservations.
        const string productId = "SKU-CONCURRENT";
        const int startingStock = 10;
        const int concurrentRequests = 20;

        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = InMemoryDbContextFactory.Create(dbName))
        {
            seedContext.InventoryItems.Add(new InventoryItem { ProductId = productId, AvailableQuantity = startingStock });
            await seedContext.SaveChangesAsync();
        }

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await using var context = InMemoryDbContextFactory.Create(dbName);
            var service = CreateService(context);
            return await service.ReserveAsync(productId, 1);
        });

        var results = await Task.WhenAll(tasks);

        var succeeded = results.Count(r => r.Outcome == OperationOutcome.Success);
        var conflicted = results.Count(r => r.Outcome == OperationOutcome.Conflict);

        Assert.Equal(startingStock, succeeded);
        Assert.Equal(concurrentRequests - startingStock, conflicted);

        await using var finalContext = InMemoryDbContextFactory.Create(dbName);
        var finalItem = await finalContext.InventoryItems.FindAsync(productId);
        Assert.Equal(0, finalItem!.AvailableQuantity);
        Assert.Equal(startingStock, finalItem.ReservedQuantity);
    }
}
