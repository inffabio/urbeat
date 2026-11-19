using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Catálogo administrativo de planos de assinatura. Somente administradores
/// autenticados escrevem; parâmetros de gateway nunca retornam segredos.
/// </summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminPlansController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly IValidator<CreatePlanRequestDto> _createPlanValidator;
    private readonly IValidator<UpdatePlanRequestDto> _updatePlanValidator;
    private readonly IValidator<UpdateGatewayConfigurationRequestDto> _gatewayValidator;

    public AdminPlansController(
        IPlanService planService,
        IValidator<CreatePlanRequestDto> createPlanValidator,
        IValidator<UpdatePlanRequestDto> updatePlanValidator,
        IValidator<UpdateGatewayConfigurationRequestDto> gatewayValidator)
    {
        _planService = planService;
        _createPlanValidator = createPlanValidator;
        _updatePlanValidator = updatePlanValidator;
        _gatewayValidator = gatewayValidator;
    }

    [HttpGet]
    [ProducesResponseType<PagedPlanResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var plans = await _planService.ListPagedAsync(
            new PlanPageQueryDto
            {
                Page = page ?? 1,
                PageSize = pageSize ?? 20
            },
            cancellationToken);

        return Ok(plans);
    }

    [HttpPost]
    [ProducesResponseType<PlanResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePlanRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _createPlanValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemFrom(validation);
        }

        var plan = await _planService.CreateAsync(request, cancellationToken);
        if (plan is null)
        {
            return Conflict(new { error = "Já existe um plano com este nome." });
        }

        return StatusCode(StatusCodes.Status201Created, plan);
    }

    [HttpPut("{planId:guid}")]
    [ProducesResponseType<PlanResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid planId,
        [FromBody] UpdatePlanRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _updatePlanValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemFrom(validation);
        }

        var plan = await _planService.UpdateAsync(planId, request, cancellationToken);
        if (plan is null)
        {
            var exists = (await _planService.ListAllAsync(cancellationToken)).Any(x => x.Id == planId);
            return exists
                ? Conflict(new { error = "Já existe um plano com este nome." })
                : NotFound();
        }

        return Ok(plan);
    }

    [HttpPatch("{planId:guid}/status")]
    [ProducesResponseType<PlanResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] Guid planId,
        [FromBody] UpdatePlanStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var plan = await _planService.UpdateStatusAsync(planId, request.IsActive, cancellationToken);
        return plan is null ? NotFound() : Ok(plan);
    }

    [HttpGet("{planId:guid}/gateway-configuration")]
    [ProducesResponseType<GatewayConfigurationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGatewayConfiguration(
        [FromRoute] Guid planId,
        CancellationToken cancellationToken)
    {
        var configuration = await _planService.GetGatewayConfigurationAsync(planId, cancellationToken);
        return configuration is null ? NotFound() : Ok(configuration);
    }

    [HttpPut("{planId:guid}/gateway-configuration")]
    [ProducesResponseType<GatewayConfigurationResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGatewayConfiguration(
        [FromRoute] Guid planId,
        [FromBody] UpdateGatewayConfigurationRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _gatewayValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblemFrom(validation);
        }

        var configuration = await _planService.UpdateGatewayConfigurationAsync(planId, request, cancellationToken);
        return configuration is null ? NotFound() : Ok(configuration);
    }

    private IActionResult ValidationProblemFrom(FluentValidation.Results.ValidationResult validation)
    {
        var errors = validation.Errors
            .GroupBy(x => x.PropertyName)
            .ToDictionary(
                keySelector: group => group.Key,
                elementSelector: group => group.Select(x => x.ErrorMessage).ToArray());

        return base.ValidationProblem(new ValidationProblemDetails(errors));
    }
}
