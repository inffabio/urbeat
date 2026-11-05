using Urbeat.Application.Security;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.SystemParameters;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Data;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly ISellerSubscriptionStatusService _sellerSubscriptionStatusService;
    private readonly ISubscriptionNotificationService _subscriptionNotificationService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IValidator<CreatePlanRequestDto> _createPlanValidator;
    private readonly IValidator<UpdatePlanRequestDto> _updatePlanValidator;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IPlanService planService,
        ISellerSubscriptionStatusService sellerSubscriptionStatusService,
        ISubscriptionNotificationService subscriptionNotificationService,
        IBackgroundJobClient backgroundJobClient,
        IValidator<CreatePlanRequestDto> createPlanValidator,
        IValidator<UpdatePlanRequestDto> updatePlanValidator,
        ILogger<AdminController> logger)
    {
        _planService = planService;
        _sellerSubscriptionStatusService = sellerSubscriptionStatusService;
        _subscriptionNotificationService = subscriptionNotificationService;
        _backgroundJobClient = backgroundJobClient;
        _createPlanValidator = createPlanValidator;
        _updatePlanValidator = updatePlanValidator;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetDashboard()
    {
        return Ok(new
        {
            area = "admin",
            message = "Admin authorized."
        });
    }

    [HttpGet("plans")]
    [ProducesResponseType<IReadOnlyList<PlanResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPlans(CancellationToken cancellationToken)
    {
        var plans = await _planService.ListAllAsync(cancellationToken);
        return Ok(plans);
    }

    [HttpPost("plans")]
    [ProducesResponseType<PlanResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePlan([FromBody] CreatePlanRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _createPlanValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = string.Join(" ", validation.Errors.Select(x => x.ErrorMessage)) });
        }

        var plan = await _planService.CreateAsync(request, cancellationToken);
        if (plan is null)
        {
            return Conflict(new { error = "A plan with this name already exists." });
        }

        return StatusCode(StatusCodes.Status201Created, plan);
    }

    [HttpPut("plans/{planId}")]
    [ProducesResponseType<PlanResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePlan([FromRoute] Guid planId, [FromBody] UpdatePlanRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _updatePlanValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = string.Join(" ", validation.Errors.Select(x => x.ErrorMessage)) });
        }

        var plan = await _planService.UpdateAsync(planId, request, cancellationToken);
        if (plan is null)
        {
            var exists = (await _planService.ListAllAsync(cancellationToken)).Any(x => x.Id == planId);
            return exists
                ? Conflict(new { error = "A plan with this name already exists." })
                : NotFound();
        }

        return Ok(plan);
    }

    [HttpPatch("plans/{planId}/status")]
    [ProducesResponseType<PlanResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlanStatus([FromRoute] Guid planId, [FromBody] UpdatePlanStatusRequestDto request, CancellationToken cancellationToken)
    {
        var plan = await _planService.UpdateStatusAsync(planId, request.IsActive, cancellationToken);
        return plan is null ? NotFound() : Ok(plan);
    }

    [HttpPost("subscriptions/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpsertSubscriptionStatus([FromBody] UpsertSellerSubscriptionStatusRequestDto request, CancellationToken cancellationToken)
    {
        await _sellerSubscriptionStatusService.UpsertAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("subscriptions/notifications/process")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ProcessSubscriptionNotifications(CancellationToken cancellationToken)
    {
        await _subscriptionNotificationService.ProcessSellerSubscriptionNotificationsAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("system-parameters")]
    [ProducesResponseType<IReadOnlyList<SystemParameterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSystemParameters([FromServices] ISystemParameterService svc, CancellationToken cancellationToken)
    {
        // Internal/ops listing. The platform settings screen must use the
        // business-scoped routes below so technical parameters never reach it.
        var all = await svc.GetAllAsync(cancellationToken);
        return Ok(all);
    }

    [HttpGet("system-parameters/business")]
    [ProducesResponseType<IReadOnlyList<SystemParameterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListBusinessSystemParameters([FromServices] ISystemParameterService svc, CancellationToken cancellationToken)
    {
        var parameters = await svc.GetBusinessAsync(cancellationToken);
        return Ok(parameters);
    }

    [HttpGet("system-parameters/business/{key}")]
    [ProducesResponseType<SystemParameterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBusinessSystemParameter([FromRoute] string key, [FromServices] ISystemParameterService svc, CancellationToken cancellationToken)
    {
        var parameter = await svc.GetBusinessByKeyAsync(key, cancellationToken);
        return parameter is null
            ? NotFound(BusinessParameterNotFound(key))
            : Ok(parameter);
    }

    [HttpPut("system-parameters/business/{key}")]
    [ProducesResponseType<SystemParameterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBusinessSystemParameter(
        [FromRoute] string key,
        [FromBody] UpdateSystemParameterRequest request,
        [FromServices] ISystemParameterService svc,
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // The business catalog is the boundary of this screen. Technical keys
        // are reported as not found, whether or not they exist in the database.
        if (!BusinessSystemParameterCatalog.Contains(key))
            return NotFound(BusinessParameterNotFound(key));

        return await UpdateSystemParameterCoreAsync(key, request, svc, dbContext, cancellationToken);
    }

    [HttpGet("system-parameters/{key}")]
    [ProducesResponseType<SystemParameterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSystemParameter([FromRoute] string key, [FromServices] ISystemParameterService svc, CancellationToken cancellationToken)
    {
        var parameter = await svc.GetByKeyAsync(key, cancellationToken);
        return parameter is null
            ? NotFound(new { error = $"Parameter '{key}' not found." })
            : Ok(parameter);
    }

    [HttpPut("system-parameters/{key}")]
    [ProducesResponseType<SystemParameterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSystemParameter(
        [FromRoute] string key,
        [FromBody] UpdateSystemParameterRequest request,
        [FromServices] ISystemParameterService svc,
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        return await UpdateSystemParameterCoreAsync(key, request, svc, dbContext, cancellationToken);
    }

    [HttpDelete("system-parameters/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSystemParameter([FromRoute] string key, [FromServices] ISystemParameterService svc, CancellationToken cancellationToken)
    {
        await svc.DeleteAsync(key, cancellationToken);
        return NoContent();
    }

    [HttpPost("system-parameters/reload")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReloadSystemParameters([FromServices] ISystemParameterService svc, CancellationToken cancellationToken)
    {
        await svc.ReloadAllAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("migrate-images")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MigrateImages(
        [FromServices] ApplicationDbContext dbContext,
        [FromServices] IOptions<CloudinaryOptions> cloudinaryOptions,
        CancellationToken cancellationToken)
    {
        var account = new Account(
            cloudinaryOptions.Value.CloudName,
            cloudinaryOptions.Value.ApiKey,
            cloudinaryOptions.Value.ApiSecret);
        var cloudinary = new Cloudinary(account);
        cloudinary.Api.Secure = true;

        var stores = await dbContext.Stores.ToListAsync(cancellationToken);
        var totalMoved = 0;
        var errors = new List<string>();
        var details = new List<object>();

        foreach (var store in stores)
        {
            var slug = store.Slug;

            var products = await dbContext.Products
                .Where(p => p.StoreId == store.Id && !string.IsNullOrEmpty(p.ImageUrl))
                .ToListAsync(cancellationToken);

            foreach (var product in products)
            {
                var publicId = ExtractPublicId(product.ImageUrl!);
                if (string.IsNullOrEmpty(publicId)) { errors.Add($"Failed to parse: {product.ImageUrl}"); continue; }

                var newPublicId = $"stores/{slug}/products/{publicId.Split('/').Last()}";
                try
                {
                    var renameResult = await cloudinary.RenameAsync(new RenameParams(publicId, newPublicId) { ResourceType = ResourceType.Image }, cancellationToken);
                    if (renameResult.Error != null)
                    {
                        errors.Add($"Rename failed for {publicId}: {renameResult.Error.Message}");
                        continue;
                    }
                    product.ImageUrl = renameResult.SecureUrl.ToString();
                    totalMoved++;
                    details.Add(new { from = publicId, to = newPublicId, url = product.ImageUrl });
                }
                catch (Exception ex)
                {
                    errors.Add($"Exception for {publicId}: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(store.LogoUrl))
            {
                var publicId = ExtractPublicId(store.LogoUrl);
                if (!string.IsNullOrEmpty(publicId))
                {
                    var newPublicId = $"stores/{slug}/store-media/{publicId.Split('/').Last()}";
                    try
                    {
                        var renameResult = await cloudinary.RenameAsync(new RenameParams(publicId, newPublicId) { ResourceType = ResourceType.Image }, cancellationToken);
                        if (renameResult.Error == null)
                        {
                            store.LogoUrl = renameResult.SecureUrl.ToString();
                            totalMoved++;
                            details.Add(new { from = publicId, to = newPublicId, url = store.LogoUrl });
                        }
                        else errors.Add($"Logo rename failed: {renameResult.Error.Message}");
                    }
                    catch (Exception ex) { errors.Add($"Logo exception: {ex.Message}"); }
                }
            }

            if (!string.IsNullOrEmpty(store.BannerUrl))
            {
                var publicId = ExtractPublicId(store.BannerUrl);
                if (!string.IsNullOrEmpty(publicId))
                {
                    var newPublicId = $"stores/{slug}/store-media/{publicId.Split('/').Last()}";
                    try
                    {
                        var renameResult = await cloudinary.RenameAsync(new RenameParams(publicId, newPublicId) { ResourceType = ResourceType.Image }, cancellationToken);
                        if (renameResult.Error == null)
                        {
                            store.BannerUrl = renameResult.SecureUrl.ToString();
                            totalMoved++;
                            details.Add(new { from = publicId, to = newPublicId, url = store.BannerUrl });
                        }
                        else errors.Add($"Banner rename failed: {renameResult.Error.Message}");
                    }
                    catch (Exception ex) { errors.Add($"Banner exception: {ex.Message}"); }
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { totalMoved, errors, details = details.Take(50) });
    }

    private async Task<IActionResult> UpdateSystemParameterCoreAsync(
        string key,
        UpdateSystemParameterRequest request,
        ISystemParameterService svc,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return BadRequest(new { error = "Value is required.", key });

        var existing = await svc.GetByKeyAsync(key, cancellationToken);
        if (existing is null)
            return NotFound(new { error = $"Parameter '{key}' not found.", key });

        if (!Enum.TryParse<SystemParameterType>(existing.Type, ignoreCase: true, out var type))
            type = SystemParameterType.String;

        var policy = SystemParameterCatalog.Resolve(existing.Key, type, existing.Group);

        if (policy.ReadOnly)
            return BadRequest(new { error = $"Parameter '{key}' is read-only through the admin API.", key });

        if (policy.Restricted && !request.ConfirmRestrictedChange)
        {
            return BadRequest(new
            {
                error = $"Parameter '{key}' is high impact. Resend the request with confirmRestrictedChange=true to apply it.",
                key,
                requiresConfirmation = true
            });
        }

        if (!SystemParameterValidator.TryNormalize(policy, type, request.Value, out var normalizedValue, out var validationError))
            return BadRequest(new { error = $"{key}: {validationError}", key });

        await svc.SetValueAsync(key, normalizedValue, cancellationToken: cancellationToken);
        // Rebuild the singleton cache from the database so every consumer sees
        // the canonical value even if another node wrote concurrently.
        await svc.ReloadAllAsync(cancellationToken);

        await WriteSystemParameterAuditAsync(dbContext, existing, cancellationToken);

        var updated = await svc.GetByKeyAsync(key, cancellationToken);
        return updated is null
            ? NotFound(new { error = $"Parameter '{key}' not found.", key })
            : Ok(updated);
    }

    private static object BusinessParameterNotFound(string key) => new
    {
        error = $"Parameter '{key}' is not available in the business settings.",
        key
    };

    private async Task WriteSystemParameterAuditAsync(
        ApplicationDbContext dbContext,
        SystemParameterDto parameter,
        CancellationToken cancellationToken)
    {
        // Audit metadata only: values (which may become secrets) are never copied
        // into the audit description. Full audit querying is a later phase.
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = Guid.TryParse(subject, out var userId) ? userId : null,
            Event = "SystemParameterUpdated",
            Entity = nameof(SystemParameter),
            EntityId = parameter.Id,
            Description = $"System parameter '{parameter.Key}' updated by an administrator.",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The parameter is already persisted. Audit history is a later phase;
            // do not turn a successful update into a failure because of the log.
            _logger.LogError(ex, "Failed to write audit log for system parameter {Key}.", parameter.Key);
        }
    }

    private static string ExtractPublicId(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.Segments;
            var uploadIndex = Array.FindIndex(segments, s => s.Equals("upload/", StringComparison.OrdinalIgnoreCase));
            if (uploadIndex == -1 || uploadIndex + 2 >= segments.Length) return string.Empty;
            var startIndex = uploadIndex + 2;
            var publicIdWithExtension = string.Join("", segments.Skip(startIndex));
            var extensionIndex = publicIdWithExtension.LastIndexOf('.');
            if (extensionIndex > 0) return publicIdWithExtension[..extensionIndex];
            return publicIdWithExtension;
        }
        catch { return string.Empty; }
    }
    private static readonly string[] RjCities =
    [
        "Angra dos Reis", "Aperibe", "Araruama", "Areal", "Armacao dos Buzios", "Arraial do Cabo",
        "Barra do Pirai", "Barra Mansa", "Belford Roxo", "Bom Jardim", "Bom Jesus do Itabapoana",
        "Cabo Frio", "Cachoeiras de Macacu", "Cambuci", "Campos dos Goytacazes", "Cantagalo",
        "Carapebus", "Cardoso Moreira", "Carmo", "Casimiro de Abreu", "Comendador Levy Gasparian",
        "Conceicao de Macabu", "Cordeiro", "Duas Barras", "Duque de Caxias", "Engenheiro Paulo de Frontin",
        "Guapimirim", "Iguaba Grande", "Itaborai", "Itaguai", "Italva", "Itaocara", "Itaperuna",
        "Itatiaia", "Japeri", "Laje do Muriae", "Macae", "Macuco", "Mage", "Mangaratiba",
        "Marica", "Mendes", "Mesquita", "Miguel Pereira", "Miracema", "Natividade", "Nilopolis",
        "Niteroi", "Nova Friburgo", "Nova Iguacu", "Paracambi", "Paraiba do Sul", "Paraty",
        "Paty do Alferes", "Petropolis", "Pinheiral", "Pirai", "Porciuncula", "Porto Real",
        "Quatis", "Queimados", "Quissama", "Resende", "Rio Bonito", "Rio Claro", "Rio das Flores",
        "Rio das Ostras", "Rio de Janeiro", "Santa Maria Madalena", "Santo Antonio de Padua",
        "Sao Fidelis", "Sao Francisco de Itabapoana", "Sao Goncalo", "Sao Joao da Barra",
        "Sao Joao de Meriti", "Sao Jose de Uba", "Sao Jose do Vale do Rio Preto", "Sao Pedro da Aldeia",
        "Sao Sebastiao do Alto", "Sapucaia", "Saquarema", "Seropedica", "Silva Jardim",
        "Sumidouro", "Tangua", "Teresopolis", "Trajano de Moraes", "Tres Rios", "Valenca",
        "Varre-Sai", "Vassouras", "Volta Redonda"
    ];

    [HttpPost("import-neighborhoods-rj")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ImportNeighborhoodsRj()
    {
        _backgroundJobClient.Enqueue<ImportUfNeighborhoodsJob>(
            job => job.ExecuteAsync("RJ", RjCities, null));
        return Ok(new { message = "Importacao OSM iniciada para 92 cidades do RJ", cities = RjCities.Length });
    }

    [HttpPost("import-neighborhoods-google")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ImportNeighborhoodsGoogle([FromQuery] string uf = "RJ")
    {
        _backgroundJobClient.Enqueue<GooglePlacesTextSearchImporter>(
            job => job.ImportAsync(uf, CancellationToken.None));
        return Ok(new { message = $"Importacao Google Places (New) iniciada para {uf}" });
    }
}

public sealed record UpdateSystemParameterRequest(string Value, bool ConfirmRestrictedChange = false);
