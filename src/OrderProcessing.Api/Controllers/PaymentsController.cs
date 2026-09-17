using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Services;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>POST /api/payments/process — process a payment for an order. Returns 200 even when
    /// the payment is declined (Status: "failed" in the response body) — the request itself was
    /// handled correctly; the caller inspects Status to decide what to do next.</summary>
    [HttpPost("process")]
    [ProducesResponseType(typeof(PaymentTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Process([FromBody] ProcessPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _paymentService.ProcessAsync(request.OrderId, request.Amount, cancellationToken);

        return result.Outcome switch
        {
            OperationOutcome.Success => Ok(PaymentTransactionResponse.FromModel(result.Value!)),
            OperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    /// <summary>GET /api/payments/{transactionId} — look up a previously processed transaction.</summary>
    [HttpGet("{transactionId}")]
    [ProducesResponseType(typeof(PaymentTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _paymentService.GetAsync(transactionId, cancellationToken);

        if (transaction is null)
        {
            return NotFound(new { message = $"Transaction '{transactionId}' was not found." });
        }

        return Ok(PaymentTransactionResponse.FromModel(transaction));
    }
}
