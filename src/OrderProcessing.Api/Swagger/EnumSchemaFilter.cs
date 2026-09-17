using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OrderProcessing.Api.Swagger;

/// <summary>
/// Swashbuckle reflects enums by their raw member names/numeric values by default, which doesn't
/// match what actually goes over the wire: Program.cs registers a JsonStringEnumConverter with
/// JsonNamingPolicy.CamelCase, so "Pending" serializes as "pending". Without this filter, the
/// generated Swagger UI/OpenAPI document would document the wrong enum casing.
/// </summary>
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
        {
            return;
        }

        schema.Enum.Clear();

        foreach (var name in Enum.GetNames(context.Type))
        {
            var camelCase = char.ToLowerInvariant(name[0]) + name[1..];
            schema.Enum.Add(new OpenApiString(camelCase));
        }

        schema.Type = "string";
        schema.Format = null;
    }
}
