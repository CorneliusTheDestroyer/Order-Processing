using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderProcessing.Api.Swagger;

/// <summary>
/// Program.cs adds the Bearer security requirement globally so every operation gets a padlock in
/// Swagger UI by default — correct for the API as a whole, but misleading on the one endpoint
/// that's actually anonymous. This filter removes the requirement from any action carrying
/// [AllowAnonymous] (or declared on a controller that does), so only AuthController's token
/// endpoint stays unlocked.
/// </summary>
public class AuthorizeCheckOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAllowAnonymous =
            context.MethodInfo.DeclaringType!.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() ||
            context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any();

        if (hasAllowAnonymous)
        {
            operation.Security.Clear();
        }
    }
}
