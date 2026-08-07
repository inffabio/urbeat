using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Urbeat.WebApi.Controllers;

[ApiController]
[Route("api/stores")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
public sealed class StoresController : ControllerBase
{
    private readonly IStoreService _storeService;
    private readonly ICuisineTypeService _cuisineTypeService;
    private readonly IStoreAddressService _storeAddressService;
    private readonly IStoreBusinessHoursService _storeBusinessHoursService;
    private readonly IValidator<CreateStoreRequestDto> _createValidator;
    private readonly IValidator<UpdateStoreRequestDto> _updateValidator;
    private readonly IValidator<UpdateStoreAddressRequestDto> _addressValidator;
    private readonly IValidator<UpsertStoreBusinessHoursRequestDto> _businessHoursValidator;
    private readonly IValidator<UpdateStoreDeliveryConfigRequestDto> _deliveryConfigValidator;
    private readonly IHubContext<Hubs.CustomerNotificationHub> _hubContext;

    public StoresController(
        IStoreService storeService,
        ICuisineTypeService cuisineTypeService,
        IStoreAddressService storeAddressService,
        IStoreBusinessHoursService storeBusinessHoursService,
        IValidator<CreateStoreRequestDto> createValidator,
        IValidator<UpdateStoreRequestDto> updateValidator,
        IValidator<UpdateStoreAddressRequestDto> addressValidator,
        IValidator<UpsertStoreBusinessHoursRequestDto> businessHoursValidator,
        IValidator<UpdateStoreDeliveryConfigRequestDto> deliveryConfigValidator,
        IHubContext<Hubs.CustomerNotificationHub> hubContext)
    {
        _storeService = storeService;
        _cuisineTypeService = cuisineTypeService;
        _storeAddressService = storeAddressService;
        _storeBusinessHoursService = storeBusinessHoursService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _addressValidator = addressValidator;
        _businessHoursValidator = businessHoursValidator;
        _deliveryConfigValidator = deliveryConfigValidator;
        _hubContext = hubContext;
    }

    [HttpGet("cuisine-types")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyCollection<CuisineTypeResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCuisineTypes(CancellationToken cancellationToken)
    {
        var types = await _cuisineTypeService.GetActiveAsync(cancellationToken);
        return Ok(types);
    }

    [HttpPost("cuisine-types")]
    [ProducesResponseType<CuisineTypeResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCuisineType([FromBody] CreateCuisineTypeRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Cuisine type name is required." });
        }

        var result = await _cuisineTypeService.CreateAsync(request.Name.Trim(), cancellationToken);
        return Created(string.Empty, result);
    }

    [HttpGet("delivery-times")]
    [ProducesResponseType<IReadOnlyCollection<DeliveryTimeResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveryTimes([FromQuery] Guid storeId, CancellationToken cancellationToken)
    {
        var times = await _storeService.GetActiveDeliveryTimesAsync(storeId, cancellationToken);
        return Ok(times);
    }

    [HttpPost("delivery-times")]
    [ProducesResponseType<DeliveryTimeResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDeliveryTime([FromBody] CreateDeliveryTimeRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _storeService.CreateDeliveryTimeAsync(request.StoreId, request.MinTimeMinutes, request.MaxTimeMinutes, cancellationToken);
        if (result is null)
            return Conflict(new { error = "Já existe um tempo de entrega com essa faixa." });

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("delivery-neighborhoods")]
    [ProducesResponseType<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveryNeighborhoods([FromQuery] string city, CancellationToken cancellationToken)
    {
        var neighborhoods = await _storeService.GetActiveDeliveryNeighborhoodsAsync(city, cancellationToken);
        return Ok(neighborhoods);
    }

    [HttpGet("delivery-neighborhoods-by-store")]
    [Authorize(Roles = "Seller")]
    [ProducesResponseType<IReadOnlyCollection<DeliveryNeighborhoodResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveryNeighborhoodsByStore([FromQuery] Guid storeId, CancellationToken cancellationToken)
    {
        var neighborhoods = await _storeService.GetActiveDeliveryNeighborhoodsByStoreAsync(storeId, cancellationToken);
        return Ok(neighborhoods);
    }

    [HttpPost("delivery-neighborhoods")]
    [ProducesResponseType<DeliveryNeighborhoodResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDeliveryNeighborhood([FromBody] CreateDeliveryNeighborhoodRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _storeService.CreateDeliveryNeighborhoodAsync(request.Neighborhood, request.City, cancellationToken);
        if (result is null)
            return Conflict(new { error = "Já existe um bairro com esse nome nesta cidade." });

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost]
    [ProducesResponseType<StoreResponseDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateStoreRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _storeService.CreateForOwnerAsync(
            ownerUserId.Value,
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (result.AlreadyExists)
        {
            return Conflict(new { error = "Seller already has a store." });
        }

        if (result.InvalidCuisineType)
        {
            return BadRequest(new { error = "Cuisine type is invalid or inactive." });
        }

        BusinessMetrics.NewStores.Inc();
        return StatusCode(StatusCodes.Status201Created, result.Store);
    }

    [HttpGet("my-store")]
    [ProducesResponseType<StoreResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyStore(CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var store = await _storeService.GetByOwnerAsync(ownerUserId.Value, cancellationToken);
        if (store is null)
        {
            return NotFound();
        }

        return Ok(store);
    }

    [HttpPut("{storeId}")]
    [ProducesResponseType<StoreResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid storeId, [FromBody] UpdateStoreRequestDto request, CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _storeService.UpdateAsync(
            ownerUserId.Value,
            storeId,
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

        if (result.InvalidCuisineType)
        {
            return BadRequest(new { error = "Cuisine type is invalid or inactive." });
        }

        return Ok(result.Store);
    }

    [HttpGet("{storeId}/address")]
    [ProducesResponseType<StoreAddressResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAddress([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var address = await _storeAddressService.GetByStoreAsync(ownerUserId.Value, storeId, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        return Ok(address);
    }

    [HttpPut("{storeId}/address")]
    [ProducesResponseType<StoreAddressResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertAddress(
        [FromRoute] Guid storeId,
        [FromBody] UpdateStoreAddressRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _addressValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _storeAddressService.UpsertAsync(
            ownerUserId.Value,
            storeId,
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

        return Ok(result.Address);
    }

    [HttpGet("{storeId}/business-hours")]
    [ProducesResponseType<StoreBusinessHoursResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBusinessHours([FromRoute] Guid storeId, CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var hours = await _storeBusinessHoursService.GetAsync(ownerUserId.Value, storeId, cancellationToken);
        if (hours is null)
        {
            return NotFound();
        }

        return Ok(hours);
    }

    [HttpPut("{storeId}/business-hours")]
    [ProducesResponseType<StoreBusinessHoursResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertBusinessHours(
        [FromRoute] Guid storeId,
        [FromBody] UpsertStoreBusinessHoursRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _businessHoursValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _storeBusinessHoursService.UpsertAsync(
            ownerUserId.Value,
            storeId,
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

        return Ok(result.Hours);
    }

    [HttpPatch("{storeId}/status")]
    [ProducesResponseType<StoreResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] Guid storeId,
        [FromBody] UpdateStoreStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _storeService.UpdateStatusAsync(
            ownerUserId.Value,
            storeId,
            request.IsOpen,
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

        if (result.SubscriptionBlocked)
        {
            return Conflict(new { error = "Store is blocked due to subscription delinquency." });
        }

        return Ok(result.Store);
    }

    [HttpPatch("{storeId}/delivery-config")]
    [ProducesResponseType<StoreResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDeliveryConfig(
        [FromRoute] Guid storeId,
        [FromBody] UpdateStoreDeliveryConfigRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = await _deliveryConfigValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(
                    keySelector: group => group.Key,
                    elementSelector: group => group.Select(x => x.ErrorMessage).ToArray())));
        }

        var ownerUserId = GetCurrentUserId();
        if (ownerUserId is null)
        {
            return Unauthorized();
        }

        var result = await _storeService.UpdateDeliveryConfigAsync(
            ownerUserId.Value,
            storeId,
            request.DeliveryFee,
            request.MinimumOrderValue,
            request.FreeShippingThreshold,
            request.FreeShippingToday,
            request.DeliveryAreas,
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

        // Notifica clientes no front da loja (ex.: tela de endereço) que a cobertura mudou.
        try { await _hubContext.Clients.Group($"store-{storeId}").SendAsync("DeliveryAreaUpdated", new { storeId }, cancellationToken); } catch { /* best-effort */ }

        return Ok(result.Store);
    }

    [HttpPost("upload-image")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB limit to match Nginx client_max_body_size
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadImage(
        [FromServices] IImageUploadService imageUploadService,
        IFormFile file,
        [FromQuery] string type = "store-media",
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Nenhum arquivo enviado." });

        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var store = await _storeService.GetByOwnerAsync(userId.Value, cancellationToken);

        var folder = store is not null
            ? $"stores/{store.Slug}/{type}"
            : $"stores/_pending/{userId}/{type}";

        try
        {
            await using var stream = file.OpenReadStream();
            var imageUrl = await imageUploadService.UploadAsync(stream, file.FileName, folder, cancellationToken);
            return Ok(new { url = imageUrl });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }
}
