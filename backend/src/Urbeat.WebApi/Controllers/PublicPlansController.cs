using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Oferta pública de planos para a landing page. Expõe somente nome, preço,
/// descrição, trial e benefícios informativos dos planos ativos.
/// </summary>
[ApiController]
[Route("api/public/plans")]
[AllowAnonymous]
public sealed class PublicPlansController : ControllerBase
{
    private readonly IPlanService _planService;

    public PublicPlansController(IPlanService planService)
    {
        _planService = planService;
    }

    [HttpGet]
    [ProducesResponseType<PublicPlanOfferResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOffer(CancellationToken cancellationToken)
    {
        var offer = await _planService.GetPublicOfferAsync(cancellationToken);
        return Ok(offer);
    }
}
