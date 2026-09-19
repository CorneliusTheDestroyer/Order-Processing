using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OrderProcessing.Api.Clients;
using OrderProcessing.Api.Configuration;
using OrderProcessing.Api.Data;
using OrderProcessing.Api.Middleware;
using OrderProcessing.Api.Services;
using OrderProcessing.Api.Swagger;
using System.Text;
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

// Bonus: JWT bearer authentication. There's no real user/account system in this domain (see
// JwtOptions and the README's Authentication section), so a single demo client-credential pair
// stands in for one; every business endpoint requires a valid token via the fallback authorization
// policy below, and AuthController's token endpoint is the one place that's [AllowAnonymous].
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");

if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
{
    // HMAC-SHA256 requires a key of at least 256 bits (32 bytes) — SymmetricSecurityKey throws on
    // first use otherwise. Failing fast here beats an opaque exception on the first token request.
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256.");
}

// Writes the same correlation-tagged RFC7807 ProblemDetails shape ApiControllerBase.ProblemResult
// produces from controllers — used by the JwtBearer events below so an auth failure looks like
// every other error this API returns, not ASP.NET Core's bare default 401/403.
static async Task WriteAuthProblemAsync(HttpContext httpContext, int statusCode, string detail)
{
    var problemDetails = new ProblemDetails
    {
        Detail = detail,
        Status = statusCode,
        Instance = httpContext.Request.Path
    };

    if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var correlationId) && correlationId is not null)
    {
        problemDetails.Extensions["correlationId"] = correlationId;
    }

    httpContext.Response.ContentType = "application/problem+json";
    await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };

        // The default JwtBearer challenge/forbid responses are a bare 401/403 with no body — this
        // keeps every auth failure in the same correlation-tagged ProblemDetails shape every other
        // error in this API already uses (see ApiControllerBase.ProblemResult).
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await WriteAuthProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "Authentication is required to access this resource.");
            },
            OnForbidden = async context =>
            {
                await WriteAuthProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                    "You do not have permission to access this resource.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

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
        Description = "Backend Developer Technical Assessment — a simplified e-commerce order " +
            "processing service with Order, Inventory, and Payment endpoints."
    });

    options.SchemaFilter<EnumSchemaFilter>();

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the raw access token returned by POST /api/auth/token — Swagger UI " +
            "adds the 'Bearer ' prefix automatically."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    options.OperationFilter<AuthorizeCheckOperationFilter>();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Program.cs uses top-level statements, which generates an internal `Program` class by default.
// The integration tests need to reference it (WebApplicationFactory<Program>) from the test
// assembly, so it's made public here — the standard pattern for testing minimal-hosting apps.
public partial class Program { }
