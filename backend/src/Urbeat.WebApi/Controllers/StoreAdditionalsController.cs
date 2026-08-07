using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/stores/{storeId}/additionals")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class StoreAdditionalsController : ControllerBase
{
    private readonly IStoreAdditionalService _service;
    private readonly IValidator<StoreAdditionalRequestDto> _validator;

    public StoreAdditionalsController(IStoreAdditionalService service, IValidator<StoreAdditionalRequestDto> validator)
    {
        _service = service;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid storeId, CancellationToken cancellationToken)
    {
        var owner = GetCurrentUserId();
        if (owner is null) return Unauthorized();
        return Ok(await _service.ListAsync(owner.Value, storeId, cancellationToken));
    }

    [HttpGet("groups")]
    public async Task<IActionResult> ListGroups(Guid storeId, CancellationToken cancellationToken)
    {
        var owner = GetCurrentUserId();
        if (owner is null) return Unauthorized();
        return Ok(await _service.ListGroupsAsync(owner.Value, storeId, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid storeId, StoreAdditionalRequestDto request, CancellationToken cancellationToken)
    {
        var owner = GetCurrentUserId();
        if (owner is null) return Unauthorized();
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.Errors.GroupBy(x => x.PropertyName).ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())));
        var result = await _service.CreateAsync(owner.Value, storeId, request, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        return CreatedAtAction(nameof(List), new { storeId }, result.Additional);
    }

    [HttpPut("{additionalId}")]
    public async Task<IActionResult> Update(Guid storeId, Guid additionalId, StoreAdditionalRequestDto request, CancellationToken cancellationToken)
    {
        var owner = GetCurrentUserId();
        if (owner is null) return Unauthorized();
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.Errors.GroupBy(x => x.PropertyName).ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).ToArray())));
        var result = await _service.UpdateAsync(owner.Value, storeId, additionalId, request, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        return Ok(result.Additional);
    }

    [HttpPatch("{additionalId}/status")]
    public async Task<IActionResult> UpdateStatus(Guid storeId, Guid additionalId, UpdateStoreAdditionalStatusRequestDto request, CancellationToken cancellationToken)
    {
        var owner = GetCurrentUserId();
        if (owner is null) return Unauthorized();
        var result = await _service.UpdateStatusAsync(owner.Value, storeId, additionalId, request.IsActive, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        return Ok(result.Additional);
    }

    [HttpDelete("{additionalId}")]
    public async Task<IActionResult> Delete(Guid storeId, Guid additionalId, CancellationToken cancellationToken)
    {
        var owner = GetCurrentUserId();
        if (owner is null) return Unauthorized();
        var result = await _service.DeleteAsync(owner.Value, storeId, additionalId, cancellationToken);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.HasProducts) return Conflict(new { error = "Não é possível excluir um adicional associado a produtos." });
        return NoContent();
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
