using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using OrderProcessing.Api.Clients;
using OrderProcessing.Api.Configuration;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Middleware;
using OrderProcessing.Api.Services;
using OrderProcessing.Api.Swagger;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// The default console formatter ignores ILogger scopes entirely, which would make
// CorrelationIdMiddleware's BeginScope call silently pointless — this is what actually gets the
// correlation id printed alongside every log line for a request.
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums (order/payment status) as their names ("pending", not 0) to match the
        // string values documented in the assessment's data models.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// [ApiController]'s automatic model-validation failures already come back as ValidationProblemDetails
// (the same ProblemDetails family ApiControllerBase.ProblemResult and ExceptionHandlingMiddleware
// use) — this just tags them with the request's correlation id too, so *every* error response,
// however it originated, carries the same trace-back-to-the-logs information.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;

    options.InvalidModelStateResponseFactory = context =>
    {
        var response = defaultFactory(context);

        if (response is ObjectResult { Value: ProblemDetails problemDetails } &&
            context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var correlationId) &&
            correlationId is not null)
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }

        return response;
    };
});

// EF Core with the In-Memory provider (mandatory technology). A single named database keeps state
// consistent across the Order/Inventory/Payment controllers for the lifetime of the process.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("OrderProcessingDb"));

// Bonus: in-memory caching on inventory reads (cache-aside, actively invalidated on every write —
// see InventoryService).
builder.Services.AddMemoryCache();

builder.Services.AddScoped<IInventoryService, InventoryService>();

// Bonus: the simulated failure rate is configuration-driven (appsettings.json, overridden per
// environment in appsettings.Development.json) instead of a hardcoded constant.
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payment"));

// Singleton: the simulator is stateless beyond its configured failure rate, and Random.Shared is
// thread-safe, so there's no need for a fresh instance per request.
builder.Services.AddSingleton<IPaymentGatewaySimulator>(sp =>
{
    var paymentOptions = sp.GetRequiredService<IOptions<PaymentOptions>>().Value;
    return new RandomPaymentGatewaySimulator(failureRate: paymentOptions.FailureRate);
});
builder.Services.AddScoped<IPaymentService, PaymentService>();

// Order talks to Inventory and Payment over real HTTP (loopback, back into this same process) via
// typed HttpClients from IHttpClientFactory — the assessment's "HTTP Client for inter-service
// communication" requirement, and the only way "service unavailability" is a real, reachable error
// scenario rather than something only a mock could produce. The base URL must match whichever
// address this API is actually listening on (see appsettings.json / launchSettings.json).
var serviceBaseUrl = builder.Configuration["ServiceEndpoints:BaseUrl"] ?? "http://localhost:5173";

builder.Services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
{
    client.BaseAddress = new Uri(serviceBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<IPaymentClient, PaymentClient>(client =>
{
    client.BaseAddress = new Uri(serviceBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<IOrderService, OrderService>();

// Bonus: Swagger/OpenAPI UI. EnumSchemaFilter re-documents enums as the camelCase strings they
// actually serialize as (see the JsonStringEnumConverter registration above), since Swashbuckle
// doesn't infer that from the converter on its own.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order Processing Service API",
        Version = "v1",
        Description = "Genasys C# Developer Technical Assessment — a simplified e-commerce order " +
            "processing service with Order, Inventory, and Payment endpoints."
    });

    options.SchemaFilter<EnumSchemaFilter>();
});

var app = builder.Build();

// Seed fixture inventory so the API is exercisable immediately after startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    DbSeeder.Seed(db);
}

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Correlation id first, so it's already on HttpContext.Items by the time the exception handler (or
// anything else) needs to read it.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Program.cs uses top-level statements, which generates an internal `Program` class by default.
// Task 8's integration tests need to reference it (WebApplicationFactory<Program>) from the test
// assembly, so it's made public here — the standard pattern for testing minimal-hosting apps.
public partial class Program { }
