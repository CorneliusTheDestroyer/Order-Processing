namespace OrderProcessing.Api.Middleware;

/// <summary>
/// Ensures every request has a correlation id: reuses an inbound `X-Correlation-Id` header if the
/// caller supplied one (useful once a request has already hopped through another service),
/// otherwise generates one. Makes it available to the rest of the pipeline via HttpContext.Items
/// (read by ExceptionHandlingMiddleware and ApiControllerBase), echoes it back as a response
/// header, and pushes it into an ILogger scope so every log line for this request is tagged with
/// it automatically — without every log call needing to pass it explicitly.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemsKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Items[ItemsKey] = correlationId;

        // Set via OnStarting rather than directly, so it's added right before headers are actually
        // sent — safe even if something later in the pipeline also touches the response headers.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
