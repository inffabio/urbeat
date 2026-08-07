using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderReportService _orderReportService;
    private readonly ICheckoutService _checkoutService;
    private readonly IOrderService _orderService;
    private readonly IValidator<CheckoutRequestDto> _checkoutValidator;
    private readonly IValidator<UpdateOrderStatusRequestDto> _updateStatusValidator;



    public OrdersController(
        ICheckoutService checkoutService,
        IOrderService orderService,
        IOrderReportService orderReportService,
        IValidator<CheckoutRequestDto> checkoutValidator,
        IValidator<UpdateOrderStatusRequestDto> updateStatusValidator)
    {
        _checkoutService = checkoutService;
        _orderService = orderService;
        _orderReportService = orderReportService;
        _checkoutValidator = checkoutValidator;
        _updateStatusValidator = updateStatusValidator;
    }
    [HttpGet("store/report")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<StoreOrdersSimpleReportResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StoreSimpleReport([FromQuery] DateTime? startDateUtc, [FromQuery] DateTime? endDateUtc, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var report = await _orderReportService.GetStoreSimpleReportAsync(sellerUserId.Value, startDateUtc, endDateUtc, cancellationToken);
        return Ok(report);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<CheckoutConfirmResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CheckoutRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _checkoutValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _checkoutService.ConfirmAsync(
            customerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.Confirmation is not null)
        {
            BusinessMetrics.OrdersCreated.Inc();
            return StatusCode(StatusCodes.Status201Created, result.Confirmation);
        }

        if (result.StoreNotFound)
        {
            return NotFound(new { error = "Store not found." });
        }

        if (result.AddressNotFound)
        {
            return NotFound(new { error = "Customer address not found." });
        }

        if (result.StoreClosed)
        {
            return Conflict(new { error = "Store is closed." });
        }

        if (result.BelowMinimum)
        {
            return BadRequest(new { error = string.Empty, summary = result.Summary });
        }

        return BadRequest();
    }

    [HttpGet("my")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<IReadOnlyCollection<OrderSummaryResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var orders = await _orderService.ListCustomerOrdersAsync(customerUserId.Value, cancellationToken);
        return Ok(orders);
    }

    [HttpGet("{orderId}")]
    [Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
    [ProducesResponseType<OrderDetailsResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid orderId, CancellationToken cancellationToken)
    {
        var customerUserId = GetCurrentUserId();
        if (customerUserId is null)
        {
            return Unauthorized();
        }

        var order = await _orderService.GetCustomerOrderAsync(customerUserId.Value, orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    [HttpGet("store")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<PagedOrderSummaryResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StoreOrders([FromQuery] StoreOrdersHistoryQueryDto query, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        if (query.EndDateUtc.HasValue && query.StartDateUtc.HasValue && query.EndDateUtc < query.StartDateUtc)
        {
            return BadRequest(new { error = "EndDateUtc must be greater than or equal to StartDateUtc." });
        }

        var orders = await _orderService.ListStoreOrdersAsync(sellerUserId.Value, query, cancellationToken);
        return Ok(orders);
    }

    [HttpGet("store/{orderId}")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<OrderDetailsResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStoreOrder([FromRoute] Guid orderId, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var order = await _orderService.GetStoreOrderAsync(sellerUserId.Value, orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    [HttpGet("store/customers")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<PagedSellerCustomerSummaryResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StoreCustomers([FromQuery] StoreCustomersQueryDto query, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var customers = await _orderService.ListStoreCustomersAsync(sellerUserId.Value, query, cancellationToken);
        return Ok(customers);
    }

    [HttpPut("store/customers/{customerUserId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    public async Task<IActionResult> UpdateStoreCustomer(Guid customerUserId, UpdateStoreCustomerRequestDto request, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();
        var result = await _orderService.UpdateStoreCustomerAsync(sellerUserId.Value, customerUserId, request, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Conflict) return Conflict(new { detail = "Já existe outro cliente com este e-mail." });
        return Ok(result.Customer);
    }

    [HttpPatch("store/customers/{customerUserId:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    public async Task<IActionResult> UpdateStoreCustomerStatus(Guid customerUserId, UpdateStoreCustomerStatusRequestDto request, CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null) return Unauthorized();
        var result = await _orderService.UpdateStoreCustomerStatusAsync(sellerUserId.Value, customerUserId, request.IsActive, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        return Ok(result.Customer);
    }

    [HttpGet("store/deliveries")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<IReadOnlyCollection<SellerDeliverySummaryResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StoreDeliveries(CancellationToken cancellationToken)
    {
        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var deliveries = await _orderService.ListStoreDeliveriesAsync(sellerUserId.Value, cancellationToken);
        return Ok(deliveries);
    }

    [HttpPatch("{orderId}/status")]
    [Authorize(Policy = AuthorizationPolicies.SellerOnly)]
    [ProducesResponseType<OrderDetailsResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] Guid orderId,
        [FromBody] UpdateOrderStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateStatusValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var sellerUserId = GetCurrentUserId();
        if (sellerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _orderService.UpdateStatusAsync(
            sellerUserId.Value,
            orderId,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Forbidden)
        {
            return Forbid();
        }

        if (result.InvalidTransition)
        {
            return BadRequest(new { error = "Invalid order status transition." });
        }

        return Ok(result.Order);
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
