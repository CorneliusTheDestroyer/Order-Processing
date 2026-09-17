using System.Net.Http.Json;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Api.Clients;

public class PaymentClient : IPaymentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentClient> _logger;

    public PaymentClient(HttpClient httpClient, ILogger<PaymentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<OperationResult<PaymentTransactionResponse>> ProcessAsync(
        Guid orderId, decimal amount, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/payments/process", new ProcessPaymentRequest { OrderId = orderId, Amount = amount }, cancellationToken);

            return await HttpResponseMapper.ToResultAsync<PaymentTransactionResponse>(response, "Payment service", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Payment service call failed for order {OrderId}.", orderId);
            return OperationResult<PaymentTransactionResponse>.Unavailable("The payment service is currently unavailable.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Payment service call timed out for order {OrderId}.", orderId);
            return OperationResult<PaymentTransactionResponse>.Unavailable("The payment service timed out.");
        }
    }
}
