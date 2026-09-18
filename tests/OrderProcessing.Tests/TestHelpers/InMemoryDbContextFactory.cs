using Microsoft.EntityFrameworkCore;
using OrderProcessing.Api.Data;

namespace OrderProcessing.Tests.TestHelpers;

/// <summary>
/// Every test gets its own uniquely-named InMemory database by default, so tests can run in
/// parallel (xUnit's default across test classes) without leaking state into each other. Passing an
/// explicit name lets a test deliberately share one store across several AppDbContext instances —
/// used by the concurrency test and anywhere a test needs to seed data in one context and read it
/// back through a fresh one, the way separate HTTP requests would.
/// </summary>
internal static class InMemoryDbContextFactory
{
    public static AppDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options);
    }

    public static AppDbContext Create() => Create(Guid.NewGuid().ToString());
}
