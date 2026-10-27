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
/// Seller route management plus the unauthenticated driver link. The store is always resolved
/// from the authenticated seller; the public side only trusts the opaque route token, whose hash
/// is the only persisted form. Expired/revoked links never expose route data.
/// </summary>
public sealed class DeliveryRouteService : IDeliveryRouteService
{
    private const int MaxPinAttempts = 5;
    private static readonly TimeSpan PinLockoutCooldown = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly EmailConfirmationOptions _emailOptions;
    private readonly DeliveryRouteOptions _routeOptions;
    private readonly ILogger<DeliveryRouteService> _logger;

    public DeliveryRouteService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork unitOfWork,
        INotificationService notificationService,
        IOutboxWriter outboxWriter,
        IOptions<EmailConfirmationOptions> emailOptions,
        IOptions<DeliveryRouteOptions> routeOptions,
        ILogger<DeliveryRouteService> logger)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
        _emailOptions = emailOptions.Value;
        _routeOptions = routeOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<DeliveryRouteResponseDto>> ListStoreRoutesAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var storeId = await FindStoreIdAsync(sellerUserId, cancellationToken);
        if (storeId is null)
        {
            return [];
        }

        var routeIds = await _dbContext.DeliveryRoutes
            .AsNoTracking()
            .Where(x => x.StoreId == storeId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return await LoadRouteDtosAsync(storeId.Value, routeIds, cancellationToken);
    }

    public async Task<DeliveryRouteOperationResultDto> GetStoreRouteAsync(
        Guid sellerUserId,
        Guid routeId,
        CancellationToken cancellationToken = default)
    {
        var storeId = await FindStoreIdAsync(sellerUserId, cancellationToken);
        if (storeId is null)
        {
            return new DeliveryRouteOperationResultDto { Forbidden = true };
        }

        var routes = await LoadRouteDtosAsync(storeId.Value, [routeId], cancellationToken);
        var route = routes.SingleOrDefault();
        return route is null
            ? new DeliveryRouteOperationResultDto { NotFound = true }
            : new DeliveryRouteOperationResultDto { Route = route };
    }

    public async Task<DeliveryRouteOperationResultDto> CreateAsync(
        Guid sellerUserId,
        CreateDeliveryRouteRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var storeId = await FindStoreIdAsync(sellerUserId, cancellationToken);
        if (storeId is null)
        {
            return new DeliveryRouteOperationResultDto { Forbidden = true };
        }

        var link = await LoadActiveLinkAsync(storeId.Value, request.DeliveryDriverId, cancellationToken);
        if (link is null)
        {
            return new DeliveryRouteOperationResultDto { DriverNotAvailable = true };
        }

        var orderIds = request.OrderIds.Distinct().ToArray();
        var validation = await ValidateOrdersAsync(
            storeId.Value,
            link,
            orderIds,
            routeId: null,
            cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var now = DateTime.UtcNow;
        var rawToken = GenerateToken();
        var route = new DeliveryRoute
        {
            StoreId = storeId.Value,
            DeliveryDriverId = link.DeliveryDriverId,
            StoreDeliveryDriverId = link.Id,
            CreatedByUserId = sellerUserId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = now.Add(GetExpiration())
        };

        await _dbContext.DeliveryRoutes.AddAsync(route, cancellationToken);
        for (var index = 0; index < orderIds.Length; index++)
        {
            await _dbContext.DeliveryRouteOrders.AddAsync(new DeliveryRouteOrder
            {
                DeliveryRouteId = route.Id,
                OrderId = orderIds[index],
                Sequence = index + 1
            }, cancellationToken);
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryRouteCreated",
            Entity = nameof(DeliveryRoute),
            EntityId = route.Id,
            Description = $"Rota criada para {link.DisplayName} com {orderIds.Length} pedido(s).",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return new DeliveryRouteOperationResultDto
            {
                Conflict = true,
                Error = "A rota não pôde ser criada porque um dos pedidos foi alterado ao mesmo tempo. Atualize e tente novamente."
            };
        }

        var created = await GetStoreRouteAsync(sellerUserId, route.Id, cancellationToken);
        return new DeliveryRouteOperationResultDto
        {
            Route = created.Route,
            ShareUrl = BuildShareUrl(rawToken)
        };
    }

    public async Task<DeliveryRouteOperationResultDto> UpdateAsync(
        Guid sellerUserId,
        Guid routeId,
        UpdateDeliveryRouteRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var storeId = await FindStoreIdAsync(sellerUserId, cancellationToken);
        if (storeId is null)
        {
            return new DeliveryRouteOperationResultDto { Forbidden = true };
        }

        var route = await _dbContext.DeliveryRoutes
            .Include(x => x.Orders)
            .SingleOrDefaultAsync(
                x => x.Id == routeId && x.StoreId == storeId,
                cancellationToken);

        if (route is null)
        {
            return new DeliveryRouteOperationResultDto { NotFound = true };
        }

        if (!route.IsActive(DateTime.UtcNow))
        {
            return new DeliveryRouteOperationResultDto
            {
                InvalidState = true,
                Error = "Somente rotas ativas podem ser atualizadas."
            };
        }

        var link = await LoadActiveLinkAsync(storeId.Value, request.DeliveryDriverId, cancellationToken);
        if (link is null)
        {
            return new DeliveryRouteOperationResultDto { DriverNotAvailable = true };
        }

        var orderIds = request.OrderIds.Distinct().ToArray();
        var validation = await ValidateOrdersAsync(
            storeId.Value,
            link,
            orderIds,
            routeId,
            cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var currentOrderIds = route.Orders.Select(x => x.OrderId).ToHashSet();
        var deliveredOrderIds = await _dbContext.Orders
            .AsNoTracking()
            .Where(x => currentOrderIds.Contains(x.Id) && x.DeliveryConfirmedAtUtc != null)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (deliveredOrderIds.Any(id => !orderIds.Contains(id)))
        {
            return new DeliveryRouteOperationResultDto
            {
                Conflict = true,
                Error = "Uma entrega já concluída não pode ser removida da rota."
            };
        }

        route.DeliveryDriverId = link.DeliveryDriverId;
        route.StoreDeliveryDriverId = link.Id;
        route.AdvanceConcurrency();
        route.MarkAsUpdated();

        var existing = route.Orders.ToDictionary(x => x.OrderId);
        foreach (var orderId in currentOrderIds.Except(orderIds))
        {
            _dbContext.DeliveryRouteOrders.Remove(existing[orderId]);
        }

        for (var index = 0; index < orderIds.Length; index++)
        {
            if (existing.TryGetValue(orderIds[index], out var stop))
            {
                stop.Sequence = index + 1;
                stop.MarkAsUpdated();
                continue;
            }

            await _dbContext.DeliveryRouteOrders.AddAsync(new DeliveryRouteOrder
            {
                DeliveryRouteId = route.Id,
                OrderId = orderIds[index],
                Sequence = index + 1
            }, cancellationToken);
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryRouteUpdated",
            Entity = nameof(DeliveryRoute),
            EntityId = route.Id,
            Description = $"Rota atualizada para {link.DisplayName} com {orderIds.Length} pedido(s).",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return new DeliveryRouteOperationResultDto
            {
                Conflict = true,
                Error = "A rota foi alterada ao mesmo tempo. Atualize e tente novamente."
            };
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return new DeliveryRouteOperationResultDto
            {
                Conflict = true,
                Error = "A rota não pôde ser atualizada porque um dos pedidos foi alterado ao mesmo tempo."
            };
        }

        return await GetStoreRouteAsync(sellerUserId, route.Id, cancellationToken);
    }

    public async Task<DeliveryRouteOperationResultDto> RevokeAsync(
        Guid sellerUserId,
        Guid routeId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var storeId = await FindStoreIdAsync(sellerUserId, cancellationToken);
        if (storeId is null)
        {
            return new DeliveryRouteOperationResultDto { Forbidden = true };
        }

        var route = await _dbContext.DeliveryRoutes
            .SingleOrDefaultAsync(
                x => x.Id == routeId && x.StoreId == storeId,
                cancellationToken);

        if (route is null)
        {
            return new DeliveryRouteOperationResultDto { NotFound = true };
        }

        if (route.RevokedAtUtc is null)
        {
            route.RevokedAtUtc = DateTime.UtcNow;
            route.AdvanceConcurrency();
            route.MarkAsUpdated();

            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                UserId = sellerUserId,
                Event = "DeliveryRouteRevoked",
                Entity = nameof(DeliveryRoute),
                EntityId = route.Id,
                Description = "Rota de entrega revogada pelo lojista.",
                IpAddress = ipAddress
            }, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await GetStoreRouteAsync(sellerUserId, route.Id, cancellationToken);
    }

    public async Task<DeliveryRouteOperationResultDto> ShareAsync(
        Guid sellerUserId,
        Guid routeId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var storeId = await FindStoreIdAsync(sellerUserId, cancellationToken);
        if (storeId is null)
        {
            return new DeliveryRouteOperationResultDto { Forbidden = true };
        }

        var route = await _dbContext.DeliveryRoutes
            .SingleOrDefaultAsync(
                x => x.Id == routeId && x.StoreId == storeId,
                cancellationToken);

        if (route is null)
        {
            return new DeliveryRouteOperationResultDto { NotFound = true };
        }

        if (!route.IsActive(DateTime.UtcNow))
        {
            return new DeliveryRouteOperationResultDto
            {
                InvalidState = true,
                Error = "Somente rotas ativas podem ser compartilhadas."
            };
        }

        // Sharing rotates the token: an older forwarded link stops working, which is the
        // revocation mechanism available to the seller without deleting the route history.
        var rawToken = GenerateToken();
        route.TokenHash = HashToken(rawToken);
        route.ExpiresAtUtc = DateTime.UtcNow.Add(GetExpiration());
        route.AdvanceConcurrency();
        route.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryRouteShared",
            Entity = nameof(DeliveryRoute),
            EntityId = route.Id,
            Description = "Link público da rota de entrega gerado pelo lojista.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await GetStoreRouteAsync(sellerUserId, route.Id, cancellationToken);
        return new DeliveryRouteOperationResultDto
        {
            Route = updated.Route,
            ShareUrl = BuildShareUrl(rawToken)
        };
    }

    public async Task<PublicDeliveryRouteAccessResultDto> GetPublicRouteAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PublicDeliveryRouteAccessResultDto { NotFound = true };
        }

        var hash = HashToken(token);
        var route = await LoadPublicRouteAsync(hash, cancellationToken);
        if (route is null)
        {
            return new PublicDeliveryRouteAccessResultDto { NotFound = true };
        }

        var status = GetRouteStatus(route, DateTime.UtcNow);
        if (status is DeliveryRouteStatus.Expired or DeliveryRouteStatus.Revoked)
        {
            return new PublicDeliveryRouteAccessResultDto { Gone = true };
        }

        return new PublicDeliveryRouteAccessResultDto
        {
            Route = await MapPublicRouteAsync(route, cancellationToken)
        };
    }

    public async Task<PublicDeliveryRoutePinResultDto> ConfirmPublicDeliveryAsync(
        string token,
        int sequence,
        string pin,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        var route = await LoadPublicRouteForUpdateAsync(HashToken(token), cancellationToken);
        if (route is null)
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        if (GetRouteStatus(route, DateTime.UtcNow) is DeliveryRouteStatus.Expired or DeliveryRouteStatus.Revoked)
        {
            return new PublicDeliveryRoutePinResultDto { Gone = true };
        }

        var stop = route.Orders.SingleOrDefault(x => x.Sequence == sequence);
        if (stop?.Order is null)
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        var order = stop.Order;

        // Retrying the same already-confirmed stop is idempotent and never counts as an error.
        if (order.DeliveryConfirmedAtUtc is not null)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                Succeeded = true,
                OrderStatus = order.Status,
                DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc,
                RemainingAttempts = Math.Max(MaxPinAttempts, 0),
                Route = await MapPublicRouteAsync(route, cancellationToken)
            };
        }

        var next = route.Orders
            .Where(x => x.Order is not null && x.Order.DeliveryConfirmedAtUtc is null)
            .OrderBy(x => x.Sequence)
            .FirstOrDefault();

        if (next is null || next.Sequence != sequence)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                InvalidState = true,
                RemainingAttempts = 0,
                Route = await MapPublicRouteAsync(route, cancellationToken)
            };
        }

        if (order.FulfillmentType != FulfillmentType.Delivery
            || order.Status is not (OrderStatus.OnDelivery or OrderStatus.Delivered))
        {
            return new PublicDeliveryRoutePinResultDto
            {
                InvalidState = true,
                RemainingAttempts = 0,
                Route = await MapPublicRouteAsync(route, cancellationToken)
            };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .SingleOrDefaultAsync(
                x => x.OrderId == order.Id
                    && x.StoreId == route.StoreId
                    && x.DeliveryDriverId == route.DeliveryDriverId,
                cancellationToken);

        if (assignment is null)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                InvalidState = true,
                RemainingAttempts = 0,
                Route = await MapPublicRouteAsync(route, cancellationToken)
            };
        }

        if (string.IsNullOrWhiteSpace(order.DeliveryPinHash))
        {
            return new PublicDeliveryRoutePinResultDto
            {
                PinUnavailable = true,
                RemainingAttempts = 0,
                Route = await MapPublicRouteAsync(route, cancellationToken)
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
                    Route = await MapPublicRouteAsync(route, cancellationToken)
                };
            }

            assignment.PinAttemptCount = 0;
            assignment.LastPinAttemptAtUtc = null;
            assignment.MarkAsUpdated();
        }

        if (pin is null
            || pin.Length != DeliveryPinCode.Length
            || !pin.All(char.IsAsciiDigit)
            || !DeliveryPinCode.Verify(order, order.DeliveryPinHash, pin))
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
                Description = $"Tentativa de PIN inválida na rota pública do pedido {order.Code} ({assignment.PinAttemptCount}).",
                IpAddress = ipAddress
            }, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await ResolvePublicPinConcurrencyAsync(token, sequence, cancellationToken);
            }

            var remaining = Math.Max(MaxPinAttempts - assignment.PinAttemptCount, 0);
            return new PublicDeliveryRoutePinResultDto
            {
                PinInvalid = remaining > 0,
                PinLocked = remaining == 0,
                RemainingAttempts = remaining,
                OrderStatus = order.Status,
                Route = await MapPublicRouteAsync(route, cancellationToken)
            };
        }

        var confirmedAtUtc = DateTime.UtcNow;
        var previousStatus = order.Status;
        order.DeliveryConfirmedAtUtc = confirmedAtUtc;
        order.DeliveryPinConfirmedAtUtc = confirmedAtUtc;
        order.DeliveryPinConfirmedByDriverId = route.DeliveryDriverId;
        if (order.Status == OrderStatus.OnDelivery)
        {
            order.Status = OrderStatus.Delivered;
            order.StatusVersion += 1;
        }

        order.MarkAsUpdated();

        assignment.AdvanceConcurrency();
        assignment.MarkAsUpdated();

        route.AdvanceConcurrency();
        route.MarkAsUpdated();

        var allDelivered = route.Orders.All(x =>
            x.OrderId == order.Id || x.Order?.DeliveryConfirmedAtUtc is not null);
        if (allDelivered)
        {
            route.CompletedAtUtc = confirmedAtUtc;
        }

        if (previousStatus != order.Status)
        {
            await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
            {
                OrderId = order.Id,
                PreviousStatus = previousStatus,
                NewStatus = order.Status,
                ChangedByUserId = Guid.Empty,
                Notes = "Entrega confirmada com PIN pelo link público da rota."
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
                    Source = "DeliveryRoute",
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
            Description = $"PIN confirmado pelo link público da rota para o pedido {order.Code}.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolvePublicPinConcurrencyAsync(token, sequence, cancellationToken);
        }

        return new PublicDeliveryRoutePinResultDto
        {
            Succeeded = true,
            OrderStatus = order.Status,
            DeliveryConfirmedAtUtc = confirmedAtUtc,
            RemainingAttempts = MaxPinAttempts,
            Route = await MapPublicRouteAsync(route, cancellationToken)
        };
    }

    private async Task<PublicDeliveryRoutePinResultDto> ResolvePublicPinConcurrencyAsync(
        string token,
        int sequence,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var route = await LoadPublicRouteForUpdateAsync(HashToken(token), cancellationToken);
        if (route is null)
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        if (GetRouteStatus(route, DateTime.UtcNow) is DeliveryRouteStatus.Expired or DeliveryRouteStatus.Revoked)
        {
            return new PublicDeliveryRoutePinResultDto { Gone = true };
        }

        var stop = route.Orders.SingleOrDefault(x => x.Sequence == sequence);
        if (stop?.Order is null)
        {
            return new PublicDeliveryRoutePinResultDto { NotFound = true };
        }

        if (stop.Order.DeliveryConfirmedAtUtc is not null)
        {
            return new PublicDeliveryRoutePinResultDto
            {
                Succeeded = true,
                OrderStatus = stop.Order.Status,
                DeliveryConfirmedAtUtc = stop.Order.DeliveryConfirmedAtUtc,
                RemainingAttempts = MaxPinAttempts,
                Route = await MapPublicRouteAsync(route, cancellationToken)
            };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == stop.OrderId, cancellationToken);

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
            OrderStatus = stop.Order.Status,
            Route = await MapPublicRouteAsync(route, cancellationToken)
        };
    }

    private async Task<DeliveryRouteOperationResultDto?> ValidateOrdersAsync(
        Guid storeId,
        StoreDeliveryDriver link,
        Guid[] orderIds,
        Guid? routeId,
        CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0)
        {
            return new DeliveryRouteOperationResultDto
            {
                OrderNotEligible = true,
                Error = "Selecione ao menos um pedido para a rota."
            };
        }

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.Id) && x.StoreId == storeId)
            .ToListAsync(cancellationToken);

        if (orders.Count != orderIds.Length)
        {
            return new DeliveryRouteOperationResultDto
            {
                OrderNotEligible = true,
                Error = "A rota contém pedidos de outra loja ou pedidos inexistentes."
            };
        }

        var assignments = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .ToDictionaryAsync(x => x.OrderId, cancellationToken);

        // A delivered stop already present in this route is history and must stay editable
        // without being treated as a new, non-deliverable order.
        var existingDeliveredStops = routeId is null
            ? new HashSet<Guid>()
            : (await _dbContext.DeliveryRouteOrders
                .AsNoTracking()
                .Where(x => x.DeliveryRouteId == routeId
                    && orderIds.Contains(x.OrderId))
                .Select(x => x.OrderId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

        foreach (var order in orders)
        {
            if (order.FulfillmentType != FulfillmentType.Delivery
                || order.SellerCompletedAtUtc is not null)
            {
                return new DeliveryRouteOperationResultDto
                {
                    OrderNotEligible = true,
                    Error = $"O pedido {order.Code} não pode entrar na rota."
                };
            }

            if (order.DeliveryConfirmedAtUtc is not null)
            {
                if (existingDeliveredStops.Contains(order.Id))
                {
                    continue;
                }

                return new DeliveryRouteOperationResultDto
                {
                    OrderNotEligible = true,
                    Error = $"O pedido {order.Code} já foi entregue e não pode entrar em uma nova rota."
                };
            }

            if (order.Status != OrderStatus.OnDelivery)
            {
                return new DeliveryRouteOperationResultDto
                {
                    OrderNotEligible = true,
                    Error = $"O pedido {order.Code} não está em entrega e não pode entrar na rota."
                };
            }

            if (!assignments.TryGetValue(order.Id, out var assignment)
                || assignment.StoreId != storeId
                || assignment.DeliveryDriverId != link.DeliveryDriverId)
            {
                return new DeliveryRouteOperationResultDto
                {
                    OrderNotEligible = true,
                    Error = $"O pedido {order.Code} não está atribuído a este motoboy."
                };
            }
        }

        var now = DateTime.UtcNow;
        var activeRouteIds = _dbContext.DeliveryRoutes
            .AsNoTracking()
            .Where(x => x.StoreId == storeId
                && x.RevokedAtUtc == null
                && x.CompletedAtUtc == null
                && x.ExpiresAtUtc > now)
            .Select(x => x.Id);

        var conflicting = await _dbContext.DeliveryRouteOrders
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId)
                && activeRouteIds.Contains(x.DeliveryRouteId)
                && (routeId == null || x.DeliveryRouteId != routeId))
            .Select(x => x.OrderId)
            .FirstOrDefaultAsync(cancellationToken);

        if (conflicting != Guid.Empty)
        {
            return new DeliveryRouteOperationResultDto
            {
                Conflict = true,
                Error = "Um dos pedidos já pertence a outra rota ativa. Revogue a rota anterior antes de reutilizá-lo."
            };
        }

        return null;
    }

    private async Task<StoreDeliveryDriver?> LoadActiveLinkAsync(
        Guid storeId,
        Guid storeDeliveryDriverId,
        CancellationToken cancellationToken)
    {
        if (storeDeliveryDriverId == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.StoreDeliveryDrivers
            .Include(x => x.DeliveryDriver)
            .SingleOrDefaultAsync(
                x => x.Id == storeDeliveryDriverId
                    && x.StoreId == storeId
                    && x.Status == StoreDeliveryDriverStatus.Active,
                cancellationToken);
    }

    private async Task<Guid?> FindStoreIdAsync(Guid sellerUserId, CancellationToken cancellationToken)
    {
        return await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.OwnerUserId == sellerUserId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<DeliveryRouteResponseDto>> LoadRouteDtosAsync(
        Guid storeId,
        IReadOnlyCollection<Guid> routeIds,
        CancellationToken cancellationToken)
    {
        if (routeIds.Count == 0)
        {
            return [];
        }

        var routes = await _dbContext.DeliveryRoutes
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && routeIds.Contains(x.Id))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (routes.Count == 0)
        {
            return [];
        }

        var linkIds = routes.Select(x => x.StoreDeliveryDriverId).Distinct().ToArray();
        var driverRows = await (
            from link in _dbContext.StoreDeliveryDrivers.AsNoTracking()
            join driver in _dbContext.DeliveryDrivers.AsNoTracking() on link.DeliveryDriverId equals driver.Id
            where linkIds.Contains(link.Id)
            select new
            {
                link.Id,
                link.DisplayName,
                DriverName = driver.FullName,
                link.PhoneNumber,
                link.WhatsAppPhoneNumber,
                DriverWhatsApp = driver.WhatsAppPhoneNumber
            }
        ).ToListAsync(cancellationToken);

        var drivers = driverRows.ToDictionary(
            x => x.Id,
            x => (
                Name: string.IsNullOrWhiteSpace(x.DisplayName) ? x.DriverName : x.DisplayName,
                WhatsApp: string.IsNullOrWhiteSpace(x.WhatsAppPhoneNumber)
                    ? x.DriverWhatsApp ?? x.PhoneNumber
                    : x.WhatsAppPhoneNumber));

        var ids = routes.Select(x => x.Id).ToArray();
        var stops = await (
            from stop in _dbContext.DeliveryRouteOrders.AsNoTracking()
            join order in _dbContext.Orders.AsNoTracking() on stop.OrderId equals order.Id
            where ids.Contains(stop.DeliveryRouteId)
            orderby stop.Sequence
            select new
            {
                stop.DeliveryRouteId,
                stop.Sequence,
                Order = order
            }
        ).ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return routes.Select(route =>
        {
            var routeStops = stops.Where(x => x.DeliveryRouteId == route.Id).OrderBy(x => x.Sequence).ToList();
            var nextSequence = routeStops
                .FirstOrDefault(x => x.Order.DeliveryConfirmedAtUtc is null)?.Sequence;
            drivers.TryGetValue(route.StoreDeliveryDriverId, out var driver);
            var status = GetRouteStatus(route, now);

            return new DeliveryRouteResponseDto
            {
                Id = route.Id,
                DeliveryDriverId = route.StoreDeliveryDriverId,
                DriverName = driver.Name ?? "Motoboy",
                DriverWhatsAppPhoneNumber = driver.WhatsApp,
                Status = status,
                CreatedAtUtc = route.CreatedAtUtc,
                ExpiresAtUtc = route.ExpiresAtUtc,
                RevokedAtUtc = route.RevokedAtUtc,
                CompletedAtUtc = route.CompletedAtUtc,
                TotalStops = routeStops.Count,
                DeliveredStops = routeStops.Count(x => x.Order.DeliveryConfirmedAtUtc is not null),
                Orders = routeStops.Select(x => new DeliveryRouteOrderResponseDto
                {
                    OrderId = x.Order.Id,
                    Sequence = x.Sequence,
                    Code = x.Order.Code,
                    AddressSummary = BuildAddressSummary(x.Order),
                    Status = x.Order.Status,
                    DeliveryConfirmedAtUtc = x.Order.DeliveryConfirmedAtUtc,
                    IsDelivered = x.Order.DeliveryConfirmedAtUtc is not null,
                    CanDeliver = x.Sequence == nextSequence
                        && (x.Order.Status == OrderStatus.OnDelivery
                            || (x.Order.Status == OrderStatus.Delivered && x.Order.DeliveryConfirmedAtUtc is null))
                        && x.Order.DeliveryConfirmedAtUtc is null
                }).ToList()
            };
        }).ToList();
    }

    private async Task<DeliveryRoute?> LoadPublicRouteAsync(
        string hash,
        CancellationToken cancellationToken)
    {
        var route = await _dbContext.DeliveryRoutes
            .AsNoTracking()
            .Include(x => x.Orders)
            .ThenInclude(x => x.Order)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        return route is null || !FixedTimeEquals(route.TokenHash, hash) ? null : route;
    }

    private async Task<DeliveryRoute?> LoadPublicRouteForUpdateAsync(
        string hash,
        CancellationToken cancellationToken)
    {
        var route = await _dbContext.DeliveryRoutes
            .Include(x => x.Orders)
            .ThenInclude(x => x.Order)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        return route is null || !FixedTimeEquals(route.TokenHash, hash) ? null : route;
    }

    private async Task<PublicDeliveryRouteResponseDto> MapPublicRouteAsync(
        DeliveryRoute route,
        CancellationToken cancellationToken)
    {
        var storeName = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.Id == route.StoreId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        var driver = await (
            from link in _dbContext.StoreDeliveryDrivers.AsNoTracking()
            join current in _dbContext.DeliveryDrivers.AsNoTracking() on link.DeliveryDriverId equals current.Id
            where link.Id == route.StoreDeliveryDriverId
            select new
            {
                link.DisplayName,
                DriverName = current.FullName
            }
        ).SingleOrDefaultAsync(cancellationToken);

        var origin = await _dbContext.StoreAddresses
            .AsNoTracking()
            .Where(x => x.StoreId == route.StoreId && x.Latitude != null && x.Longitude != null)
            .Select(x => new { Latitude = x.Latitude!.Value, Longitude = x.Longitude!.Value })
            .FirstOrDefaultAsync(cancellationToken);

        var orderedStops = route.Orders.OrderBy(x => x.Sequence).ToList();
        var customerIds = orderedStops
            .Where(x => x.Order is not null)
            .Select(x => x.Order!.CustomerUserId)
            .Distinct()
            .ToArray();
        var names = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.UserId) && x.ClaimType == "FullName")
            .Select(x => new { x.UserId, x.ClaimValue })
            .ToDictionaryAsync(x => x.UserId, x => x.ClaimValue, cancellationToken);
        var phones = await _dbContext.Users
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PhoneNumber })
            .ToDictionaryAsync(x => x.Id, x => x.PhoneNumber, cancellationToken);

        var next = orderedStops
            .Where(x => x.Order is not null && x.Order.DeliveryConfirmedAtUtc is null)
            .OrderBy(x => x.Sequence)
            .FirstOrDefault();

        var status = GetRouteStatus(route, DateTime.UtcNow);
        return new PublicDeliveryRouteResponseDto
        {
            StoreName = storeName,
            DriverName = string.IsNullOrWhiteSpace(driver?.DisplayName)
                ? driver?.DriverName ?? "Motoboy"
                : driver.DisplayName,
            Status = status,
            ExpiresAtUtc = route.ExpiresAtUtc,
            CompletedAtUtc = route.CompletedAtUtc,
            OriginLatitude = origin?.Latitude,
            OriginLongitude = origin?.Longitude,
            NextSequence = next?.Sequence,
            TotalStops = orderedStops.Count,
            DeliveredStops = orderedStops.Count(x => x.Order?.DeliveryConfirmedAtUtc is not null),
            Stops = orderedStops.Select(x => new PublicDeliveryRouteStopDto
            {
                Sequence = x.Sequence,
                OrderCode = x.Order?.Code ?? string.Empty,
                Status = x.Order?.Status ?? OrderStatus.Cancelled,
                IsNext = x.Sequence == next?.Sequence,
                CanDeliver = x.Sequence == next?.Sequence
                    && (x.Order?.Status == OrderStatus.OnDelivery
                        || (x.Order?.Status == OrderStatus.Delivered && x.Order?.DeliveryConfirmedAtUtc is null))
                    && x.Order?.DeliveryConfirmedAtUtc is null,
                CustomerName = x.Order is null ? null : names.GetValueOrDefault(x.Order.CustomerUserId),
                CustomerPhoneNumber = x.Order is null ? null : phones.GetValueOrDefault(x.Order.CustomerUserId),
                AddressCep = x.Order?.AddressCep,
                AddressStreet = x.Order?.AddressStreet,
                AddressNumber = x.Order?.AddressNumber,
                AddressNeighborhood = x.Order?.AddressNeighborhood,
                AddressCity = x.Order?.AddressCity,
                AddressState = x.Order?.AddressState,
                AddressComplement = x.Order?.AddressComplement,
                AddressReference = x.Order?.AddressReference,
                AddressSummary = x.Order is null ? null : BuildAddressSummary(x.Order),
                DestinationLatitude = x.Order?.DeliveryLatitude,
                DestinationLongitude = x.Order?.DeliveryLongitude,
                Notes = x.Order?.Notes,
                DeliveryConfirmedAtUtc = x.Order?.DeliveryConfirmedAtUtc
            }).ToList()
        };
    }

    private static DeliveryRouteStatus GetRouteStatus(DeliveryRoute route, DateTime utcNow)
    {
        if (route.RevokedAtUtc is not null) return DeliveryRouteStatus.Revoked;
        if (route.CompletedAtUtc is not null) return DeliveryRouteStatus.Completed;
        return route.ExpiresAtUtc <= utcNow ? DeliveryRouteStatus.Expired : DeliveryRouteStatus.Active;
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
        var path = _routeOptions.PublicPath.Trim('/');
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

    private static string? BuildAddressSummary(Order order)
    {
        var streetLine = string.Join(", ", new[] { order.AddressStreet, order.AddressNumber }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var parts = new[] { streetLine, order.AddressNeighborhood }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        var summary = string.Join(" - ", parts);
        return string.IsNullOrWhiteSpace(summary) ? null : summary;
    }
}
