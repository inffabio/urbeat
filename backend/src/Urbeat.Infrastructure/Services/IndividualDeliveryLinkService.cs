using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Shares and confirms a single delivery through a temporary public link, without creating a
/// route. The seller is always resolved from the authenticated identity; the public link only
/// accepts the opaque token (persisted as a hash) bound to one order and one driver. The delivery
/// PIN never leaves the customer/seller-authenticated contracts.
/// </summary>
public sealed class IndividualDeliveryLinkService : IIndividualDeliveryLinkService
{
    private const int MaxPinAttempts = 5;
    private static readonly TimeSpan PinLockoutCooldown = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly EmailConfirmationOptions _emailOptions;
    private readonly DeliveryRouteOptions _routeOptions;
    private readonly ILogger<IndividualDeliveryLinkService> _logger;

    public IndividualDeliveryLinkService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork unitOfWork,
        INotificationService notificationService,
        IOutboxWriter outboxWriter,
        IOptions<EmailConfirmationOptions> emailOptions,
        IOptions<DeliveryRouteOptions> routeOptions,
        ILogger<IndividualDeliveryLinkService> logger)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
        _emailOptions = emailOptions.Value;
        _routeOptions = routeOptions.Value;
        _logger = logger;
    }

    public async Task<ShareIndividualDeliveryResultDto> ShareAsync(
        Guid sellerUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new ShareIndividualDeliveryResultDto { NotFound = true };
        }

        var ownsStore = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == order.StoreId && x.OwnerUserId == sellerUserId, cancellationToken);

        if (!ownsStore)
        {
            return new ShareIndividualDeliveryResultDto { Forbidden = true };
        }

        if (order.FulfillmentType != FulfillmentType.Delivery
            || order.Status != OrderStatus.OnDelivery
            || order.SellerCompletedAtUtc is not null
            || order.DeliveryConfirmedAtUtc is not null)
        {
            return new ShareIndividualDeliveryResultDto
            {
                InvalidState = true,
                Error = "Só é possível compartilhar uma entrega que já saiu para entrega."
            };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        if (assignment is null || assignment.DeliveryStartedAtUtc is null)
        {
            return new ShareIndividualDeliveryResultDto
            {
                InvalidState = true,
                Error = "Atribua um motoboy e inicie a entrega antes de compartilhar o link."
            };
        }

        var driverIsActive = await _dbContext.StoreDeliveryDrivers
            .AsNoTracking()
            .AnyAsync(
                x => x.StoreId == order.StoreId
                    && x.DeliveryDriverId == assignment.DeliveryDriverId
                    && x.Status == StoreDeliveryDriverStatus.Active,
                cancellationToken);

        if (!driverIsActive)
        {
            return new ShareIndividualDeliveryResultDto
            {
                InvalidState = true,
                Error = "O motoboy desta entrega não está ativo."
            };
        }

        var rawToken = GenerateToken();
        order.DeliveryLinkTokenHash = HashToken(rawToken);
        order.DeliveryLinkExpiresAtUtc = DateTime.UtcNow.Add(GetExpiration());
        order.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "IndividualDeliveryLinkShared",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Link individual de entrega gerado para o pedido {order.Code}.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return new ShareIndividualDeliveryResultDto
            {
                InvalidState = true,
                Error = "A entrega foi alterada ao mesmo tempo. Atualize o pedido e tente novamente."
            };
        }

        _logger.LogInformation(
            "Individual delivery link shared | OrderId={OrderId} | StoreId={StoreId}",
            order.Id,
            order.StoreId);

        return new ShareIndividualDeliveryResultDto
        {
            ShareUrl = BuildShareUrl(rawToken)
        };
    }

    public async Task<PublicDeliveryRouteAccessResultDto> GetPublicDeliveryAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadPublicOrderAsync(token, cancellationToken);
        if (order is null)
        {
            return new PublicDeliveryRouteAccessResultDto { NotFound = true };
        }

        if (IsLinkUnavailable(order, DateTime.UtcNow))
        {
            return new PublicDeliveryRouteAccessResultDto { Gone = true };
        }

        return new PublicDeliveryRouteAccessResultDto
        {
            Route = await MapPublicDeliveryAsync(order, cancellationToken)
        };
    }

    public async Task<PublicDeliveryRoutePinResultDto> ConfirmPublicDeliveryAsync(
        string token,
        string pin,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadPublicOrderForUpdateAsync(token, cancellationToken);
        if (order is null)
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        if (IsLinkUnavailable(order, DateTime.UtcNow))
        {
            return new PublicDeliveryRoutePinResultDto { Gone = true };
        }

        // Repeating a confirmation is idempotent and never counts as an error.
        if (order.DeliveryConfirmedAtUtc is not null)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                Succeeded = true,
                OrderStatus = order.Status,
                DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc,
                RemainingAttempts = MaxPinAttempts,
                Route = await MapPublicDeliveryAsync(order, cancellationToken)
            };
        }

        if (order.Status is not (OrderStatus.OnDelivery or OrderStatus.Delivered))
        {
            return new PublicDeliveryRoutePinResultDto
            {
                InvalidState = true,
                RemainingAttempts = 0,
                Route = await MapPublicDeliveryAsync(order, cancellationToken)
            };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        if (assignment is null
            || assignment.DeliveryStartedAtUtc is null
            || assignment.StoreId != order.StoreId)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                InvalidState = true,
                RemainingAttempts = 0,
                Route = await MapPublicDeliveryAsync(order, cancellationToken)
            };
        }

        if (string.IsNullOrWhiteSpace(order.DeliveryPinHash))
        {
            return new PublicDeliveryRoutePinResultDto
            {
                PinUnavailable = true,
                RemainingAttempts = 0,
                Route = await MapPublicDeliveryAsync(order, cancellationToken)
            };
        }

        var attemptAtUtc = DateTime.UtcNow;
        if (assignment.PinAttemptCount >= MaxPinAttempts)
        {
            var lockExpiresAtUtc = (assignment.LastPinAttemptAtUtc ?? attemptAtUtc).Add(PinLockoutCooldown);
            if (lockExpiresAtUtc > attemptAtUtc)
            {
                return new PublicDeliveryRoutePinResultDto
                {
                    PinLocked = true,
                    RemainingAttempts = 0,
                    Route = await MapPublicDeliveryAsync(order, cancellationToken)
                };
            }

            assignment.PinAttemptCount = 0;
            assignment.LastPinAttemptAtUtc = null;
            assignment.MarkAsUpdated();
        }

        var verified = pin is not null
            && pin.Length == DeliveryPinCode.Length
            && pin.All(char.IsAsciiDigit)
            && DeliveryPinCode.Verify(order, order.DeliveryPinHash, pin);

        if (!verified)
        {
            assignment.PinAttemptCount = Math.Min(assignment.PinAttemptCount + 1, MaxPinAttempts);
            assignment.LastPinAttemptAtUtc = attemptAtUtc;
            assignment.AdvanceConcurrency();
            assignment.MarkAsUpdated();

            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                UserId = null,
                Event = "DeliveryPinRejected",
                Entity = nameof(Order),
                EntityId = order.Id,
                Description = $"Tentativa de PIN inválida no link individual do pedido {order.Code} ({assignment.PinAttemptCount}).",
                IpAddress = ipAddress
            }, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await ResolvePublicPinConcurrencyAsync(token, cancellationToken);
            }

            var remaining = Math.Max(MaxPinAttempts - assignment.PinAttemptCount, 0);
            return new PublicDeliveryRoutePinResultDto
            {
                PinInvalid = remaining > 0,
                PinLocked = remaining == 0,
                RemainingAttempts = remaining,
                OrderStatus = order.Status,
                Route = await MapPublicDeliveryAsync(order, cancellationToken)
            };
        }

        var confirmedAtUtc = DateTime.UtcNow;
        var previousStatus = order.Status;
        order.DeliveryConfirmedAtUtc = confirmedAtUtc;
        order.DeliveryPinConfirmedAtUtc = confirmedAtUtc;
        order.DeliveryPinConfirmedByDriverId = assignment.DeliveryDriverId;
        if (order.Status == OrderStatus.OnDelivery)
        {
            order.Status = OrderStatus.Delivered;
            order.StatusVersion += 1;
        }

        order.MarkAsUpdated();
        assignment.AdvanceConcurrency();
        assignment.MarkAsUpdated();

        if (previousStatus != order.Status)
        {
            await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
            {
                OrderId = order.Id,
                PreviousStatus = previousStatus,
                NewStatus = order.Status,
                ChangedByUserId = Guid.Empty,
                Notes = "Entrega confirmada com PIN pelo link individual."
            }, cancellationToken);

            await _notificationService.NotifyCustomerOrderStatusChangedAsync(
                order.CustomerUserId,
                order.Id,
                order.Status,
                $"Seu pedido {order.Code} foi entregue.",
                cancellationToken);

            await _outboxWriter.EnqueueAsync(
                OutboxEventTypes.OrderStatusChanged,
                order.Id,
                new OrderStatusChangedEvent
                {
                    OrderId = order.Id,
                    StoreId = order.StoreId,
                    CustomerUserId = order.CustomerUserId,
                    SellerUserId = await GetStoreOwnerAsync(order.StoreId, cancellationToken) ?? Guid.Empty,
                    Code = order.Code,
                    PreviousStatus = previousStatus,
                    NewStatus = order.Status,
                    ChangedAtUtc = confirmedAtUtc,
                    Source = "IndividualDeliveryLink",
                    Sequence = order.StatusVersion
                },
                confirmedAtUtc,
                sequence: order.StatusVersion,
                aggregateType: nameof(Order),
                cancellationToken: cancellationToken);
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = null,
            Event = "DeliveryPinConfirmed",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"PIN confirmado pelo link individual da entrega do pedido {order.Code}.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolvePublicPinConcurrencyAsync(token, cancellationToken);
        }

        _logger.LogInformation(
            "Individual delivery confirmed with PIN | OrderId={OrderId} | StoreId={StoreId}",
            order.Id,
            order.StoreId);

        return new PublicDeliveryRoutePinResultDto
        {
            Succeeded = true,
            OrderStatus = order.Status,
            DeliveryConfirmedAtUtc = confirmedAtUtc,
            RemainingAttempts = MaxPinAttempts,
            Route = await MapPublicDeliveryAsync(order, cancellationToken)
        };
    }

    private async Task<PublicDeliveryRoutePinResultDto> ResolvePublicPinConcurrencyAsync(
        string token,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var order = await LoadPublicOrderAsync(token, cancellationToken);
        if (order is null)
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        if (IsLinkUnavailable(order, DateTime.UtcNow))
        {
            return new PublicDeliveryRoutePinResultDto { Gone = true };
        }

        if (order.DeliveryConfirmedAtUtc is not null)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                Succeeded = true,
                OrderStatus = order.Status,
                DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc,
                RemainingAttempts = MaxPinAttempts,
                Route = await MapPublicDeliveryAsync(order, cancellationToken)
            };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        var attemptCount = assignment?.PinAttemptCount ?? 0;
        var cooldownExpired = attemptCount >= MaxPinAttempts
            && (assignment?.LastPinAttemptAtUtc ?? DateTime.UtcNow).Add(PinLockoutCooldown) <= DateTime.UtcNow;
        var effectiveAttemptCount = cooldownExpired ? 0 : attemptCount;
        var locked = attemptCount >= MaxPinAttempts && !cooldownExpired;
        var remaining = locked ? 0 : Math.Max(MaxPinAttempts - effectiveAttemptCount, 0);

        return new PublicDeliveryRoutePinResultDto
        {
            ConcurrentUpdate = true,
            PinLocked = locked,
            PinInvalid = !locked,
            RemainingAttempts = remaining,
            OrderStatus = order.Status,
            Route = await MapPublicDeliveryAsync(order, cancellationToken)
        };
    }

    private async Task<Order?> LoadPublicOrderAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = HashToken(token);
        var order = await _dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DeliveryLinkTokenHash == hash, cancellationToken);

        return order is null || !FixedTimeEquals(order.DeliveryLinkTokenHash!, hash) ? null : order;
    }

    private async Task<Order?> LoadPublicOrderForUpdateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = HashToken(token);
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.DeliveryLinkTokenHash == hash, cancellationToken);

        return order is null || !FixedTimeEquals(order.DeliveryLinkTokenHash!, hash) ? null : order;
    }

    private static bool IsLinkUnavailable(Order order, DateTime utcNow)
    {
        return order.DeliveryLinkExpiresAtUtc is null
            || order.DeliveryLinkExpiresAtUtc <= utcNow
            || order.Status == OrderStatus.Cancelled;
    }

    private async Task<PublicDeliveryRouteResponseDto> MapPublicDeliveryAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var storeName = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.Id == order.StoreId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        var assignment = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        var driver = assignment is null
            ? null
            : await (
                from link in _dbContext.StoreDeliveryDrivers.AsNoTracking()
                join current in _dbContext.DeliveryDrivers.AsNoTracking()
                    on link.DeliveryDriverId equals current.Id
                where link.StoreId == order.StoreId
                    && link.DeliveryDriverId == assignment.DeliveryDriverId
                select new
                {
                    link.DisplayName,
                    DriverName = current.FullName
                }
            ).SingleOrDefaultAsync(cancellationToken);

        var origin = await _dbContext.StoreAddresses
            .AsNoTracking()
            .Where(x => x.StoreId == order.StoreId && x.Latitude != null && x.Longitude != null)
            .Select(x => new { Latitude = x.Latitude!.Value, Longitude = x.Longitude!.Value })
            .FirstOrDefaultAsync(cancellationToken);

        var customerName = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => x.UserId == order.CustomerUserId && x.ClaimType == "FullName")
            .Select(x => x.ClaimValue)
            .SingleOrDefaultAsync(cancellationToken);

        var customerPhoneNumber = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == order.CustomerUserId)
            .Select(x => x.PhoneNumber)
            .SingleOrDefaultAsync(cancellationToken);

        var utcNow = DateTime.UtcNow;
        var status = order.DeliveryConfirmedAtUtc is not null
            ? DeliveryRouteStatus.Completed
            : DeliveryRouteStatus.Active;
        var canDeliver = order.DeliveryConfirmedAtUtc is null
            && (order.Status == OrderStatus.OnDelivery
                || (order.Status == OrderStatus.Delivered && order.DeliveryConfirmedAtUtc is null))
            && assignment?.DeliveryStartedAtUtc is not null
            && order.DeliveryLinkExpiresAtUtc > utcNow;

        return new PublicDeliveryRouteResponseDto
        {
            StoreName = storeName,
            DriverName = string.IsNullOrWhiteSpace(driver?.DisplayName)
                ? driver?.DriverName ?? "Motoboy"
                : driver.DisplayName,
            Status = status,
            ExpiresAtUtc = order.DeliveryLinkExpiresAtUtc ?? utcNow,
            CompletedAtUtc = order.DeliveryConfirmedAtUtc,
            OriginLatitude = origin?.Latitude,
            OriginLongitude = origin?.Longitude,
            NextSequence = canDeliver ? 1 : null,
            TotalStops = 1,
            DeliveredStops = order.DeliveryConfirmedAtUtc is null ? 0 : 1,
            Stops =
            [
                new PublicDeliveryRouteStopDto
                {
                    Sequence = 1,
                    OrderCode = order.Code,
                    Status = order.Status,
                    IsNext = canDeliver,
                    CanDeliver = canDeliver,
                    CustomerName = customerName,
                    CustomerPhoneNumber = customerPhoneNumber,
                    AddressCep = order.AddressCep,
                    AddressStreet = order.AddressStreet,
                    AddressNumber = order.AddressNumber,
                    AddressNeighborhood = order.AddressNeighborhood,
                    AddressCity = order.AddressCity,
                    AddressState = order.AddressState,
                    AddressComplement = order.AddressComplement,
                    AddressReference = order.AddressReference,
                    AddressSummary = BuildAddressSummary(order),
                    DestinationLatitude = order.DeliveryLatitude,
                    DestinationLongitude = order.DeliveryLongitude,
                    Notes = order.Notes,
                    DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc
                }
            ]
        };
    }

    private static string? BuildAddressSummary(Order order)
    {
        var streetLine = string.Join(", ", new[] { order.AddressStreet, order.AddressNumber }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { streetLine, order.AddressNeighborhood }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var summary = string.Join(" - ", parts);
        return string.IsNullOrWhiteSpace(summary) ? null : summary;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private TimeSpan GetExpiration()
    {
        var hours = Math.Clamp(
            _routeOptions.ExpirationHours,
            Math.Max(_routeOptions.MinExpirationHours, 1),
            Math.Max(_routeOptions.MaxExpirationHours, 1));
        return TimeSpan.FromHours(hours);
    }

    private string BuildShareUrl(string rawToken)
    {
        var baseUrl = _emailOptions.FrontendBaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(_routeOptions.IndividualPublicPath)
            ? "entrega"
            : _routeOptions.IndividualPublicPath.Trim('/');
        return $"{baseUrl}/{path}/{Uri.EscapeDataString(rawToken)}";
    }

    private static string GenerateToken()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private async Task<Guid?> GetStoreOwnerAsync(Guid storeId, CancellationToken cancellationToken)
    {
        return await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.Id == storeId)
            .Select(x => (Guid?)x.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
