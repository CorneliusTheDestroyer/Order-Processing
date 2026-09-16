using Microsoft.EntityFrameworkCore;
using OrderProcessing.Api.Models;

namespace OrderProcessing.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);

            // An order owns its line items; deleting an order deletes its items with it.
            order.HasMany(o => o.Items)
                 .WithOne()
                 .HasForeignKey(i => i.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>().HasKey(i => i.Id);

        modelBuilder.Entity<InventoryItem>(inventory =>
        {
            inventory.HasKey(i => i.ProductId);

            // Optimistic concurrency: every update bumps RowVersion, so two requests that both
            // read the same starting quantity can't both succeed in over-reserving stock.
            inventory.Property(i => i.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<PaymentTransaction>().HasKey(p => p.TransactionId);
    }
}
