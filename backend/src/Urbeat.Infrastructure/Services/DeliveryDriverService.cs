using Microsoft.EntityFrameworkCore;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Seller-side operational motoboy management: registration of name/phone, activation, deletion
/// and order assignment. The motoboy has no account, password or session; every delivery is
/// operated through the public bearer links handled by <c>DeliveryRouteService</c> and
/// <c>IndividualDeliveryLinkService</c>. Every operation is scoped to the authenticated seller's
/// store.
/// </summary>
public sealed class DeliveryDriverService : IDeliveryDriverService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _unitOfWork;
    private readonly IOrderService _orderService;

    public DeliveryDriverService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork unitOfWork,
        IOrderService orderService)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _orderService = orderService;
    }

    public async Task<IReadOnlyCollection<SellerDeliveryDriverResponseDto>> ListStoreDriversAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);

        if (store is null)
        {
            return [];
        }

        var rows = await (
            from link in _dbContext.StoreDeliveryDrivers.AsNoTracking()
            join driver in _dbContext.DeliveryDrivers.AsNoTracking() on link.DeliveryDriverId equals driver.Id
            where link.StoreId == store.Id
            orderby link.CreatedAtUtc descending
            select new { link, driver }
        ).ToListAsync(cancellationToken);

        return rows.Select(x => MapSellerDriver(x.link, x.driver)).ToList();
    }

    public async Task<SellerDeliveryDriverOperationResultDto> CreateStoreDriverAsync(
        Guid sellerUserId,
        UpsertStoreDeliveryDriverRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);

        if (store is null)
        {
            return new SellerDeliveryDriverOperationResultDto { Forbidden = true };
        }

        var name = request.Name.Trim();
        var phone = NormalizePhone(request.PhoneNumber);
        var whatsApp = NormalizePhoneOrNull(request.WhatsAppPhoneNumber);

        var driver = new DeliveryDriver
        {
            FullName = name,
            PhoneNumber = phone,
            WhatsAppPhoneNumber = whatsApp
        };

        var link = new StoreDeliveryDriver
        {
            StoreId = store.Id,
            DeliveryDriverId = driver.Id,
            DeliveryDriver = driver,
            DisplayName = name,
            PhoneNumber = phone,
            WhatsAppPhoneNumber = whatsApp,
            Status = request.IsActive ? StoreDeliveryDriverStatus.Active : StoreDeliveryDriverStatus.Revoked,
            AcceptedAtUtc = request.IsActive ? DateTime.UtcNow : null,
            RevokedAtUtc = request.IsActive ? null : DateTime.UtcNow,
            InvitedByUserId = sellerUserId
        };

        await _dbContext.DeliveryDrivers.AddAsync(driver, cancellationToken);
        await _dbContext.StoreDeliveryDrivers.AddAsync(link, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryDriverCreated",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = $"Motoboy {name} cadastrado na loja {store.Id}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SellerDeliveryDriverOperationResultDto
        {
            Driver = MapSellerDriver(link, driver)
        };
    }

    public async Task<SellerDeliveryDriverOperationResultDto> UpdateStoreDriverAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        UpsertStoreDeliveryDriverRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);

        if (store is null)
        {
            return new SellerDeliveryDriverOperationResultDto { Forbidden = true };
        }

        var link = await _dbContext.StoreDeliveryDrivers
            .Include(x => x.DeliveryDriver)
            .SingleOrDefaultAsync(
                x => x.Id == storeDriverId && x.StoreId == store.Id,
                cancellationToken);

        if (link?.DeliveryDriver is null)
        {
            return new SellerDeliveryDriverOperationResultDto { NotFound = true };
        }

        var name = request.Name.Trim();
        var phone = NormalizePhone(request.PhoneNumber);
        var whatsApp = NormalizePhoneOrNull(request.WhatsAppPhoneNumber);

        if (link.Status == StoreDeliveryDriverStatus.Active && !request.IsActive)
        {
            var blocked = await HasDeliveryPendingCompletionAsync(link, store.Id, cancellationToken);
            if (blocked is not null)
            {
                return blocked;
            }
        }

        link.DisplayName = name;
        link.PhoneNumber = phone;
        link.WhatsAppPhoneNumber = whatsApp;
        ApplyActiveState(link, request.IsActive);
        link.MarkAsUpdated();

        link.DeliveryDriver.FullName = name;
        link.DeliveryDriver.PhoneNumber = phone;
        link.DeliveryDriver.WhatsAppPhoneNumber = whatsApp;
        link.DeliveryDriver.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryDriverUpdated",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = $"Motoboy {name} atualizado na loja {store.Id}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SellerDeliveryDriverOperationResultDto
        {
            Driver = MapSellerDriver(link, link.DeliveryDriver)
        };
    }

    public async Task<SellerDeliveryDriverOperationResultDto> SetStoreDriverActiveAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        bool isActive,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);

        if (store is null)
        {
            return new SellerDeliveryDriverOperationResultDto { Forbidden = true };
        }

        var link = await _dbContext.StoreDeliveryDrivers
            .Include(x => x.DeliveryDriver)
            .SingleOrDefaultAsync(
                x => x.Id == storeDriverId && x.StoreId == store.Id,
                cancellationToken);

        if (link?.DeliveryDriver is null)
        {
            return new SellerDeliveryDriverOperationResultDto { NotFound = true };
        }

        if (!isActive)
        {
            var blocked = await HasDeliveryPendingCompletionAsync(link, store.Id, cancellationToken);
            if (blocked is not null)
            {
                return blocked;
            }
        }

        ApplyActiveState(link, isActive);
        link.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = isActive ? "DeliveryDriverActivated" : "DeliveryDriverDeactivated",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = isActive
                ? $"Motoboy {link.DisplayName} ativado na loja {store.Id}."
                : $"Motoboy {link.DisplayName} inativado na loja {store.Id}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SellerDeliveryDriverOperationResultDto
        {
            Driver = MapSellerDriver(link, link.DeliveryDriver)
        };
    }

    /// <summary>
    /// A driver cannot be deactivated while an assigned order still depends on the link to finish
    /// (delivery started or delivered awaiting PIN). Revoked access would strand the order.
    /// </summary>
    private async Task<SellerDeliveryDriverOperationResultDto?> HasDeliveryPendingCompletionAsync(
        StoreDeliveryDriver link,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var hasDeliveryPendingCompletion = await (
            from assignment in _dbContext.OrderDeliveryAssignments.AsNoTracking()
            join order in _dbContext.Orders.AsNoTracking() on assignment.OrderId equals order.Id
            where assignment.DeliveryDriverId == link.DeliveryDriverId
                && assignment.StoreId == storeId
                && (order.Status == OrderStatus.OnDelivery
                    || (order.Status == OrderStatus.Delivered && order.DeliveryConfirmedAtUtc == null))
            select assignment.Id
        ).AnyAsync(cancellationToken);

        if (!hasDeliveryPendingCompletion)
        {
            return null;
        }

        return new SellerDeliveryDriverOperationResultDto
        {
            Conflict = true,
            Error = "Este motoboy possui entrega em andamento ou aguardando confirmação de PIN. Conclua esses pedidos antes de inativar."
        };
    }

    private static void ApplyActiveState(StoreDeliveryDriver link, bool isActive)
    {
        if (isActive)
        {
            link.Status = StoreDeliveryDriverStatus.Active;
            link.RevokedAtUtc = null;
            link.AcceptedAtUtc ??= DateTime.UtcNow;
            return;
        }

        link.Status = StoreDeliveryDriverStatus.Revoked;
        link.RevokedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes only the link owned by the authenticated seller's store. A driver that already
    /// appears in any order assignment or delivery route is kept for history and must be
    /// deactivated instead. The global identity is removed only when no other store still uses it.
    /// </summary>
    public async Task<SellerDeliveryDriverOperationResultDto> DeleteStoreDriverAsync(
        Guid sellerUserId,
        Guid storeDriverId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);

        if (store is null)
        {
            return new SellerDeliveryDriverOperationResultDto { Forbidden = true };
        }

        var link = await _dbContext.StoreDeliveryDrivers
            .Include(x => x.DeliveryDriver)
            .SingleOrDefaultAsync(
                x => x.Id == storeDriverId && x.StoreId == store.Id,
                cancellationToken);

        if (link?.DeliveryDriver is null)
        {
            return new SellerDeliveryDriverOperationResultDto { NotFound = true };
        }

        var driver = link.DeliveryDriver;

        var hasOrders = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .AnyAsync(
                x => x.DeliveryDriverId == driver.Id && x.StoreId == store.Id,
                cancellationToken);

        if (hasOrders)
        {
            return new SellerDeliveryDriverOperationResultDto
            {
                Conflict = true,
                Error = "Este motoboy possui pedidos vinculados e não pode ser excluído. Inative-o para impedir novas entregas."
            };
        }

        var hasRoutes = await _dbContext.DeliveryRoutes
            .AsNoTracking()
            .AnyAsync(x => x.StoreDeliveryDriverId == link.Id, cancellationToken);

        if (hasRoutes)
        {
            return new SellerDeliveryDriverOperationResultDto
            {
                Conflict = true,
                Error = "Este motoboy possui rotas vinculadas e não pode ser excluído. Inative-o para impedir novas entregas."
            };
        }

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryDriverDeleted",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = $"Motoboy {link.DisplayName} excluído da loja {store.Id}.",
            IpAddress = ipAddress
        }, cancellationToken);

        _dbContext.StoreDeliveryDrivers.Remove(link);

        // Removing the link must never delete an identity still used elsewhere.
        var hasOtherLinks = await _dbContext.StoreDeliveryDrivers
            .AsNoTracking()
            .AnyAsync(x => x.DeliveryDriverId == driver.Id && x.Id != link.Id, cancellationToken);

        var hasOtherOrders = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .AnyAsync(x => x.DeliveryDriverId == driver.Id, cancellationToken);

        var hasOtherRoutes = await _dbContext.DeliveryRoutes
            .AsNoTracking()
            .AnyAsync(
                x => x.DeliveryDriverId == driver.Id && x.StoreDeliveryDriverId != link.Id,
                cancellationToken);

        if (!hasOtherLinks && !hasOtherOrders && !hasOtherRoutes)
        {
            _dbContext.DeliveryDrivers.Remove(driver);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SellerDeliveryDriverOperationResultDto();
    }

    private static string NormalizePhone(string phone) => BrazilianMobilePhone.Normalize(phone);

    private static string? NormalizePhoneOrNull(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : BrazilianMobilePhone.Normalize(phone);

    public async Task<AssignDeliveryDriverResultDto> AssignAsync(
        Guid sellerUserId,
        Guid orderId,
        Guid deliveryDriverId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new AssignDeliveryDriverResultDto { NotFound = true };
        }

        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == order.StoreId, cancellationToken);

        if (store is null || store.OwnerUserId != sellerUserId)
        {
            return new AssignDeliveryDriverResultDto { Forbidden = true };
        }

        if (order.FulfillmentType != FulfillmentType.Delivery
            || order.Status is not (OrderStatus.Ready or OrderStatus.OnDelivery)
            || order.SellerCompletedAtUtc is not null)
        {
            return new AssignDeliveryDriverResultDto { InvalidState = true };
        }

        var link = await _dbContext.StoreDeliveryDrivers
            .Include(x => x.DeliveryDriver)
            .SingleOrDefaultAsync(
                x => x.Id == deliveryDriverId && x.StoreId == store.Id,
                cancellationToken);

        if (link?.DeliveryDriver is null)
        {
            return new AssignDeliveryDriverResultDto { DriverNotAvailable = true };
        }

        if (link.Status != StoreDeliveryDriverStatus.Active)
        {
            return new AssignDeliveryDriverResultDto { DriverNotAvailable = true };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        if (assignment is not null
            && (assignment.DeliveryStartedAtUtc is not null || order.Status == OrderStatus.OnDelivery))
        {
            return new AssignDeliveryDriverResultDto { InvalidState = true };
        }

        var assignedAtUtc = DateTime.UtcNow;

        if (assignment is null)
        {
            assignment = new OrderDeliveryAssignment
            {
                OrderId = order.Id,
                StoreId = order.StoreId,
                DeliveryDriverId = link.DeliveryDriverId,
                AssignedByUserId = sellerUserId,
                AssignedAtUtc = assignedAtUtc
            };
            await _dbContext.OrderDeliveryAssignments.AddAsync(assignment, cancellationToken);
        }
        else
        {
            assignment.DeliveryDriverId = link.DeliveryDriverId;
            assignment.AssignedByUserId = sellerUserId;
            assignment.AssignedAtUtc = assignedAtUtc;
            assignment.DeliveryStartedAtUtc = null;
            assignment.PinAttemptCount = 0;
            assignment.LastPinAttemptAtUtc = null;
            assignment.AdvanceConcurrency();
            assignment.MarkAsUpdated();
        }

        order.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "OrderDeliveryDriverAssigned",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Order {order.Code} assigned to delivery driver {link.DeliveryDriver.FullName}.",
            IpAddress = ipAddress
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique index on OrderId: another request won the assignment race.
            return new AssignDeliveryDriverResultDto { InvalidState = true };
        }

        return new AssignDeliveryDriverResultDto
        {
            Order = await _orderService.GetStoreOrderAsync(sellerUserId, order.Id, cancellationToken)
        };
    }

    public async Task<AssignDeliveryDriverResultDto> RemoveAssignmentAsync(
        Guid sellerUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return new AssignDeliveryDriverResultDto { NotFound = true };
        }

        var store = await _dbContext.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == order.StoreId, cancellationToken);

        if (store is null || store.OwnerUserId != sellerUserId)
        {
            return new AssignDeliveryDriverResultDto { Forbidden = true };
        }

        if (order.FulfillmentType != FulfillmentType.Delivery)
        {
            return new AssignDeliveryDriverResultDto { InvalidState = true };
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);

        if (assignment is null)
        {
            return new AssignDeliveryDriverResultDto
            {
                Order = await _orderService.GetStoreOrderAsync(sellerUserId, order.Id, cancellationToken)
            };
        }

        if (assignment.DeliveryStartedAtUtc is not null
            || order.Status is not (OrderStatus.Ready or OrderStatus.OnDelivery))
        {
            return new AssignDeliveryDriverResultDto { InvalidState = true };
        }

        _dbContext.OrderDeliveryAssignments.Remove(assignment);
        order.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "OrderDeliveryDriverAssignmentRemoved",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Delivery driver assignment removed from order {order.Code}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AssignDeliveryDriverResultDto
        {
            Order = await _orderService.GetStoreOrderAsync(sellerUserId, order.Id, cancellationToken)
        };
    }

    private static SellerDeliveryDriverResponseDto MapSellerDriver(StoreDeliveryDriver link, DeliveryDriver driver)
    {
        return new SellerDeliveryDriverResponseDto
        {
            Id = link.Id,
            DeliveryDriverId = driver.Id,
            Name = string.IsNullOrWhiteSpace(link.DisplayName) ? driver.FullName : link.DisplayName,
            PhoneNumber = string.IsNullOrWhiteSpace(link.PhoneNumber) ? driver.PhoneNumber : link.PhoneNumber,
            WhatsAppPhoneNumber = string.IsNullOrWhiteSpace(link.WhatsAppPhoneNumber)
                ? driver.WhatsAppPhoneNumber ?? (string.IsNullOrWhiteSpace(link.PhoneNumber) ? driver.PhoneNumber : link.PhoneNumber)
                : link.WhatsAppPhoneNumber,
            IsActive = link.Status == StoreDeliveryDriverStatus.Active,
            Status = link.Status,
            AcceptedAtUtc = link.AcceptedAtUtc,
            RevokedAtUtc = link.RevokedAtUtc
        };
    }
}
