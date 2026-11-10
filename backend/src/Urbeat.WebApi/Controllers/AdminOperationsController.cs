using Urbeat.Application.Security;
using Urbeat.WebApi.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Urbeat.WebApi.Controllers;

/// <summary>
/// Administrative observability surface. Every route is AdminOnly; responses contain aggregate
/// counters, UTC timestamps, dependency status and worker liveness only.
/// </summary>
[ApiController]
[Route("api/admin/operations")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminOperationsController : ControllerBase
{
    private readonly OutboxOperationsService _operations;

    public AdminOperationsController(OutboxOperationsService operations)
    {
        _operations = operations;
    }

    [HttpGet("outbox")]
    [ProducesResponseType<OutboxOperationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OutboxOperationsResponse>> GetOutboxOperations(
        CancellationToken cancellationToken)
    {
        var response = await _operations.GetAsync(cancellationToken).ConfigureAwait(false);
        return Ok(response);
    }
}
