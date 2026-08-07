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
[Route("api/customer/addresses")]
[Authorize(Policy = AuthorizationPolicies.CustomerOnly)]
public sealed class CustomerAddressesController : ControllerBase
{
    private readonly ICustomerAddressService _customerAddressService;
    private readonly IValidator<UpsertCustomerAddressRequestDto> _validator;

    public CustomerAddressesController(
        ICustomerAddressService customerAddressService,
        IValidator<UpsertCustomerAddressRequestDto> validator)
    {
        _customerAddressService = customerAddressService;
        _validator = validator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<CustomerAddressResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var addresses = await _customerAddressService.ListAsync(userId.Value, cancellationToken);
        return Ok(addresses);
    }

    [HttpPost]
    [ProducesResponseType<CustomerAddressResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] UpsertCustomerAddressRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _customerAddressService.CreateAsync(
            userId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.LimitReached)
        {
            return Conflict(new { error = "Customer can have at most 3 addresses." });
        }

        return StatusCode(StatusCodes.Status201Created, result.Address);
    }

    [HttpPut("{addressId}")]
    [ProducesResponseType<CustomerAddressResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid addressId, [FromBody] UpsertCustomerAddressRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _customerAddressService.UpdateAsync(
            userId.Value,
            addressId,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.NotFound)
        {
            return NotFound();
        }

        return Ok(result.Address);
    }

    [HttpDelete("{addressId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] Guid addressId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var deleted = await _customerAddressService.DeleteAsync(
            userId.Value,
            addressId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
