using Microsoft.EntityFrameworkCore;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums (order/payment status) as their names ("pending", not 0) to match the
        // string values documented in the assessment's data models.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// EF Core with the In-Memory provider (mandatory technology). A single named database keeps state
// consistent across the Order/Inventory/Payment controllers for the lifetime of the process.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("OrderProcessingDb"));

builder.Services.AddScoped<IInventoryService, InventoryService>();

var app = builder.Build();

// Seed fixture inventory so the API is exercisable immediately after startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbSeeder.Seed(db);
}

// Configure the HTTP request pipeline.

app.UseAuthorization();

app.MapControllers();

app.Run();
