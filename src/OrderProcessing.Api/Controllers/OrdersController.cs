using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Common;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Services;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>POST /api/orders — create a new order, running it through the full
    /// reserve-inventory -> take-payment -> confirm/cancel flow before returning.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.CreateAsync(request, cancellationToken);

        return result.Outcome switch
        {
            OperationOutcome.Success => CreatedAtAction(
                nameof(GetById), new { id = result.Value!.Id }, OrderResponse.FromModel(result.Value)),
            OperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
            OperationOutcome.Conflict => Conflict(new { message = result.Error }),
            OperationOutcome.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.Error }),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    /// <summary>GET /api/orders/{id} — retrieve order details.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetByIdAsync(id, cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = $"Order '{id}' was not found." });
        }

        return Ok(OrderResponse.FromModel(order));
    }

    /// <summary>GET /api/orders — list orders, paginated (page is 1-based; pageSize is clamped to
    /// [1, 100]).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var paged = await _orderService.ListAsync(page, pageSize, cancellationToken);

        var response = new PagedResult<OrderResponse>
        {
            Items = paged.Items.Select(OrderResponse.FromModel).ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalCount = paged.TotalCount
        };

        return Ok(response);
    }

    /// <summary>PUT /api/orders/{id}/status — transition an order to a new status, subject to the
    /// state machine in OrderService (e.g. a Shipped order can't be reopened to Pending).</summary>
    [HttpPut("{id}/status")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.UpdateStatusAsync(id, request.Status, cancellationToken);

        return result.Outcome switch
        {
            OperationOutcome.Success => Ok(OrderResponse.FromModel(result.Value!)),
            OperationOutcome.NotFound => NotFound(new { message = result.Error }),
            OperationOutcome.Conflict => Conflict(new { message = result.Error }),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
