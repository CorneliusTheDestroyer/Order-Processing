using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Models;
using OrderProcessing.Api.Services;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ApiControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(IInventoryService inventoryService, ILogger<InventoryController> logger)
    {
        _inventoryService = inventoryService;
        _logger = logger;
    }

    /// <summary>GET /api/inventory/{productId} — check product availability.</summary>
    [HttpGet("{productId}")]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(string productId, CancellationToken cancellationToken)
    {
        var item = await _inventoryService.GetAsync(productId, cancellationToken);

        if (item is null)
        {
            _logger.LogInformation("Inventory lookup miss for product {ProductId}.", productId);
            return ProblemResult($"Product '{productId}' was not found in inventory.", StatusCodes.Status404NotFound);
        }

        return Ok(InventoryItemResponse.FromModel(item));
    }

    /// <summary>POST /api/inventory/{productId}/reserve — reserve items against an order.</summary>
    [HttpPost("{productId}/reserve")]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reserve(
        string productId, [FromBody] InventoryQuantityRequest request, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.ReserveAsync(productId, request.Quantity, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>POST /api/inventory/{productId}/release — release a previous reservation
    /// (e.g. because payment for the order subsequently failed).</summary>
    [HttpPost("{productId}/release")]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Release(
        string productId, [FromBody] InventoryQuantityRequest request, CancellationToken cancellationToken)
    {
        var result = await _inventoryService.ReleaseAsync(productId, request.Quantity, cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult(OperationResult<InventoryItem> result) => result.Outcome switch
    {
        OperationOutcome.Success => Ok(InventoryItemResponse.FromModel(result.Value!)),
        OperationOutcome.NotFound => ProblemResult(result.Error!, StatusCodes.Status404NotFound),
        OperationOutcome.ValidationFailed => ProblemResult(result.Error!, StatusCodes.Status400BadRequest),
        // 409: the request is well-formed, but conflicts with current inventory state (insufficient
        // stock, or releasing more than is reserved) — a business-state conflict, not bad input.
        OperationOutcome.Conflict => ProblemResult(result.Error!, StatusCodes.Status409Conflict),
        _ => ProblemResult("An unexpected error occurred.", StatusCodes.Status500InternalServerError)
    };
}
