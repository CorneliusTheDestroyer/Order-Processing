using System.Net.Http.Json;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Api.Clients;

public class InventoryClient : IInventoryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient httpClient, ILogger<InventoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<OperationResult<InventoryItemResponse>> ReserveAsync(
        string productId, int quantity, CancellationToken cancellationToken = default)
        => PostQuantityAsync($"api/inventory/{Uri.EscapeDataString(productId)}/reserve", quantity, cancellationToken);

    public Task<OperationResult<InventoryItemResponse>> ReleaseAsync(
        string productId, int quantity, CancellationToken cancellationToken = default)
        => PostQuantityAsync($"api/inventory/{Uri.EscapeDataString(productId)}/release", quantity, cancellationToken);

    private async Task<OperationResult<InventoryItemResponse>> PostQuantityAsync(
        string requestUri, int quantity, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                requestUri, new InventoryQuantityRequest { Quantity = quantity }, cancellationToken);

            return await HttpResponseMapper.ToResultAsync<InventoryItemResponse>(response, "Inventory service", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Inventory service call to {RequestUri} failed.", requestUri);
            return OperationResult<InventoryItemResponse>.Unavailable("The inventory service is currently unavailable.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Distinguishes our own request timeout from the caller cancelling the operation.
            _logger.LogError(ex, "Inventory service call to {RequestUri} timed out.", requestUri);
            return OperationResult<InventoryItemResponse>.Unavailable("The inventory service timed out.");
        }
    }
}
