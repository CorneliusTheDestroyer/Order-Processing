using Microsoft.AspNetCore.Mvc;

namespace OrderProcessing.Api.Middleware;

/// <summary>
/// Last line of defense: catches anything that escapes controller-level handling (a bug, an
/// unexpected EF Core error, a downstream call throwing something the HTTP clients didn't already
/// catch) and turns it into a consistent ProblemDetails response instead of a bare 500 or a leaked
/// stack trace. Registered after CorrelationIdMiddleware, so the correlation id is already on
/// HttpContext.Items by the time anything reaches here.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var value)
                ? value?.ToString()
                : null;

            _logger.LogError(
                ex, "Unhandled exception processing {Method} {Path} (correlation id: {CorrelationId}).",
                context.Request.Method, context.Request.Path, correlationId);

            if (context.Response.HasStarted)
            {
                // The response has already started streaming — we can't overwrite it with a
                // ProblemDetails body, so rethrow and let the server's own diagnostics take over
                // rather than silently swallowing the error.
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problemDetails = new ProblemDetails
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Instance = context.Request.Path,
                // Only leak exception detail outside Production — this is exactly the kind of thing
                // that shouldn't reach a real caller's error log in a deployed environment.
                Detail = _environment.IsDevelopment()
                    ? ex.ToString()
                    : "An unexpected error occurred while processing your request."
            };

            if (correlationId is not null)
            {
                problemDetails.Extensions["correlationId"] = correlationId;
            }

            await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
        }
    }
}
