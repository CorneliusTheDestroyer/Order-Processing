using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderProcessing.Api.Common;

namespace OrderProcessing.Api.Clients;

/// <summary>
/// Translates a downstream controller's HTTP response into an OperationResult, so InventoryClient
/// and PaymentClient don't each reimplement the same status-code mapping.
/// </summary>
internal static class HttpResponseMapper
{
    // System.Net.Http.Json's ReadFromJsonAsync uses its own default JsonSerializerOptions unless
    // told otherwise — that default happens to match property names case-insensitively, but it has
    // no enum-as-string converter. Program.cs's MVC responses serialize enums (OrderStatus,
    // PaymentStatus) as camelCase strings via JsonStringEnumConverter, so without registering the
    // same converter here, deserializing a real response — e.g. PaymentTransactionResponse's
    // "status":"completed" — throws a JsonException instead of parsing it. This was only ever
    // exercised by Task 8's end-to-end tests (unit tests mock the HTTP clients entirely), and would
    // otherwise have made every real, successful payment call crash in production.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<OperationResult<T>> ToResultAsync<T>(
        HttpResponseMessage response, string serviceName, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value is null
                ? OperationResult<T>.Unavailable($"{serviceName} returned an empty success response.")
                : OperationResult<T>.Success(value);
        }

        var message = await ReadErrorMessageAsync(response, serviceName, cancellationToken);

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => OperationResult<T>.NotFound(message),
            HttpStatusCode.BadRequest => OperationResult<T>.ValidationFailed(message),
            HttpStatusCode.Conflict => OperationResult<T>.Conflict(message),
            _ => OperationResult<T>.Unavailable(message)
        };
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response, string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            // Our controllers return the standard ASP.NET Core ProblemDetails shape
            // ({ "detail": "...", "status": ..., ... }), not a bespoke { "message": "..." } body —
            // read the "detail" field so Order actually surfaces Inventory/Payment's real business
            // error text (e.g. "Insufficient stock for 'SKU-004'...") instead of always falling back
            // to the generic message below.
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions, cancellationToken);
            return body?.Detail ?? $"{serviceName} returned HTTP {(int)response.StatusCode}.";
        }
        catch (Exception)
        {
            // Response body wasn't the ProblemDetails shape our controllers use (or wasn't JSON at
            // all) — fall back to a generic message instead of letting deserialization failure blow
            // up what is already an error path.
            return $"{serviceName} returned HTTP {(int)response.StatusCode}.";
        }
    }

    private class ErrorBody
    {
        public string? Detail { get; set; }
    }
}
