using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Middleware;

namespace OrderProcessing.Api.Controllers;

/// <summary>
/// Base for our controllers so that every non-2xx response — across Inventory, Payments, and
/// Orders alike — shares one consistent, correlation-id-tagged ProblemDetails shape instead of each
/// controller inventing its own ad hoc error body.
/// </summary>
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult ProblemResult(string detail, int statusCode)
    {
        var problemDetails = new ProblemDetails
        {
            Detail = detail,
            Status = statusCode,
            Instance = HttpContext.Request.Path
        };

        if (HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var correlationId) && correlationId is not null)
        {
            problemDetails.Extensions["correlationId"] = correlationId;
        }

        return new ObjectResult(problemDetails) { StatusCode = statusCode };
    }
}
