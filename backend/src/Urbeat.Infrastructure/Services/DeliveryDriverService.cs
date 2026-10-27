using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Outbox;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Authoritative delivery driver workflow: store-scoped invites, driver portal reads and
/// seller manual assignment. Every read/write filtered by the authenticated user (seller owns
/// the store or driver has an Active link to the store) — the storeId supplied by a client is
/// only a selector and is always validated against the authenticated identity.
/// </summary>
public sealed class DeliveryDriverService : IDeliveryDriverService
{
    public const string DeliveryDriverRole = "DeliveryDriver";

    private const int MaxPinAttempts = 5;
    private static readonly TimeSpan PinLockoutCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _unitOfWork;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IEncryptionService _encryptionService;
    private readonly INotificationService _notificationService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IOrderService _orderService;
    private readonly EmailConfirmationOptions _emailOptions;
    private readonly ILogger<DeliveryDriverService> _logger;

    public DeliveryDriverService(
        ApplicationDbContext dbContext,
        IEfUnitOfWork unitOfWork,
        UserManager<IdentityUser<Guid>> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IEncryptionService encryptionService,
        INotificationService notificationService,
        IOutboxWriter outboxWriter,
        IOrderService orderService,
        IOptions<EmailConfirmationOptions> emailOptions,
        ILogger<DeliveryDriverService> logger)
    {
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _userManager = userManager;
        _roleManager = roleManager;
        _encryptionService = encryptionService;
        _notificationService = notificationService;
        _outboxWriter = outboxWriter;
        _orderService = orderService;
        _emailOptions = emailOptions.Value;
        _logger = logger;
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
            WhatsAppPhoneNumber = whatsApp,
            Email = null,
            NormalizedEmail = null,
            UserId = null
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
            link.InviteTokenHash = null;
            link.InviteExpiresAtUtc = null;
            return;
        }

        link.Status = StoreDeliveryDriverStatus.Revoked;
        link.RevokedAtUtc = DateTime.UtcNow;
        link.InviteTokenHash = null;
        link.InviteExpiresAtUtc = null;
    }

    private static string NormalizePhone(string phone) => phone.Trim();

    private static string? NormalizePhoneOrNull(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    public async Task<SellerDeliveryDriverOperationResultDto> InviteAsync(
        Guid sellerUserId,
        InviteDeliveryDriverRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.OwnerUserId == sellerUserId, cancellationToken);

        if (store is null)
        {
            return new SellerDeliveryDriverOperationResultDto { Forbidden = true };
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var normalizedEmail = email.ToUpperInvariant();
        var fullName = request.FullName.Trim();
        var phone = request.PhoneNumber.Trim();

        var driver = await _dbContext.DeliveryDrivers
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (driver is null)
        {
            driver = new DeliveryDriver
            {
                Email = email,
                NormalizedEmail = normalizedEmail,
                FullName = fullName,
                PhoneNumber = phone,
                WhatsAppPhoneNumber = phone
            };
            await _dbContext.DeliveryDrivers.AddAsync(driver, cancellationToken);
        }

        var link = await _dbContext.StoreDeliveryDrivers
            .SingleOrDefaultAsync(
                x => x.StoreId == store.Id && x.DeliveryDriverId == driver.Id,
                cancellationToken);

        if (link is { Status: StoreDeliveryDriverStatus.Active })
        {
            return new SellerDeliveryDriverOperationResultDto
            {
                Conflict = true,
                Error = "Este entregador já está ativo nesta loja."
            };
        }

        if (link is null)
        {
            link = new StoreDeliveryDriver
            {
                StoreId = store.Id,
                DeliveryDriverId = driver.Id,
                DeliveryDriver = driver
            };
            await _dbContext.StoreDeliveryDrivers.AddAsync(link, cancellationToken);
        }

        link.DisplayName = fullName;
        link.PhoneNumber = phone;
        link.WhatsAppPhoneNumber = phone;
        link.Status = StoreDeliveryDriverStatus.Pending;
        link.RevokedAtUtc = null;
        link.AcceptedAtUtc = null;
        link.InvitedByUserId = sellerUserId;
        link.MarkAsUpdated();

        await IssueInviteAsync(link, store, driver, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryDriverInvited",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = $"Delivery driver {email} invited to store {store.Id}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SellerDeliveryDriverOperationResultDto
        {
            Driver = MapSellerDriver(link, driver)
        };
    }

    public async Task<SellerDeliveryDriverOperationResultDto> ResendInviteAsync(
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

        if (link.Status == StoreDeliveryDriverStatus.Active)
        {
            return new SellerDeliveryDriverOperationResultDto
            {
                Conflict = true,
                Error = "O entregador já aceitou o convite."
            };
        }

        link.Status = StoreDeliveryDriverStatus.Pending;
        link.RevokedAtUtc = null;
        link.MarkAsUpdated();

        await IssueInviteAsync(link, store, link.DeliveryDriver, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = sellerUserId,
            Event = "DeliveryDriverInviteResent",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = $"Delivery driver invite resent to {link.DeliveryDriver.Email ?? link.DisplayName}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SellerDeliveryDriverOperationResultDto
        {
            Driver = MapSellerDriver(link, link.DeliveryDriver)
        };
    }

    public async Task<SellerDeliveryDriverOperationResultDto> RevokeAsync(
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

        if (link.Status != StoreDeliveryDriverStatus.Revoked)
        {
            // A driver may only be revoked when no assigned order still depends on this link
            // to finish the delivery. Delivered orders awaiting PIN confirmation are included:
            // revoking now would leave the order without the driver that can confirm the PIN.
            var hasDeliveryPendingCompletion = await (
                from assignment in _dbContext.OrderDeliveryAssignments.AsNoTracking()
                join order in _dbContext.Orders.AsNoTracking() on assignment.OrderId equals order.Id
                where assignment.DeliveryDriverId == link.DeliveryDriverId
                    && assignment.StoreId == store.Id
                    && (order.Status == OrderStatus.OnDelivery
                        || (order.Status == OrderStatus.Delivered && order.DeliveryConfirmedAtUtc == null))
                select assignment.Id
            ).AnyAsync(cancellationToken);

            if (hasDeliveryPendingCompletion)
            {
                return new SellerDeliveryDriverOperationResultDto
                {
                    Conflict = true,
                    Error = "Este entregador possui entrega em andamento ou aguardando confirmação de PIN. Conclua esses pedidos antes de revogar o acesso."
                };
            }

            link.Status = StoreDeliveryDriverStatus.Revoked;
            link.RevokedAtUtc = DateTime.UtcNow;
            link.InviteTokenHash = null;
            link.InviteExpiresAtUtc = null;
            link.MarkAsUpdated();

            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                UserId = sellerUserId,
                Event = "DeliveryDriverRevoked",
                Entity = nameof(StoreDeliveryDriver),
                EntityId = link.Id,
                Description = $"Delivery driver access revoked for {link.DeliveryDriver.Email ?? link.DisplayName} in store {store.Id}.",
                IpAddress = ipAddress
            }, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new SellerDeliveryDriverOperationResultDto
        {
            Driver = MapSellerDriver(link, link.DeliveryDriver)
        };
    }

    public async Task<DeliveryDriverInvitePreviewResponseDto> GetInvitePreviewAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var link = await FindInviteByTokenAsync(token, cancellationToken);
        if (link?.DeliveryDriver is null)
        {
            return new DeliveryDriverInvitePreviewResponseDto { Valid = false };
        }

        var storeName = await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.Id == link.StoreId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        if (link.Status == StoreDeliveryDriverStatus.Active)
        {
            return new DeliveryDriverInvitePreviewResponseDto
            {
                Valid = true,
                AlreadyAccepted = true,
                StoreName = storeName,
                DriverName = link.DeliveryDriver.FullName,
                MaskedEmail = MaskEmail(link.DeliveryDriver.Email),
                ExpiresAtUtc = link.InviteExpiresAtUtc
            };
        }

        var isValid = link.Status == StoreDeliveryDriverStatus.Pending
            && link.InviteExpiresAtUtc is not null
            && link.InviteExpiresAtUtc > DateTime.UtcNow;

        return new DeliveryDriverInvitePreviewResponseDto
        {
            Valid = isValid,
            StoreName = storeName,
            DriverName = link.DeliveryDriver.FullName,
            MaskedEmail = MaskEmail(link.DeliveryDriver.Email),
            ExpiresAtUtc = link.InviteExpiresAtUtc
        };
    }

    public async Task<AcceptDeliveryDriverInviteResultDto> AcceptInviteAsync(
        string token,
        AcceptDeliveryDriverInviteRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var link = await FindInviteByTokenAsync(token, cancellationToken);
        if (link?.DeliveryDriver is null
            || link.Status != StoreDeliveryDriverStatus.Pending
            || link.InviteExpiresAtUtc is null
            || link.InviteExpiresAtUtc <= DateTime.UtcNow)
        {
            return new AcceptDeliveryDriverInviteResultDto
            {
                InvalidInvite = true,
                Error = "Convite inválido ou expirado."
            };
        }

        var driver = link.DeliveryDriver;
        if (string.IsNullOrWhiteSpace(driver.Email))
        {
            return new AcceptDeliveryDriverInviteResultDto
            {
                InvalidInvite = true,
                Error = "Convite inválido ou expirado."
            };
        }

        var email = driver.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new IdentityUser<Guid>
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                PhoneNumber = driver.PhoneNumber,
                LockoutEnabled = true
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return new AcceptDeliveryDriverInviteResultDto
                {
                    Error = string.Join("; ", createResult.Errors.Select(x => x.Description))
                };
            }

            var claimResult = await _userManager.AddClaimAsync(
                user,
                new System.Security.Claims.Claim("FullName",
                    string.IsNullOrWhiteSpace(request.FullName) ? driver.FullName : request.FullName.Trim()));
            if (!claimResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                return new AcceptDeliveryDriverInviteResultDto { Error = "Não foi possível concluir o convite." };
            }
        }
        else
        {
            // The password proves ownership of the existing account. The response is deliberately
            // generic so the endpoint cannot be used to enumerate accounts.
            if (!await _userManager.CheckPasswordAsync(user, request.Password))
            {
                _logger.LogWarning(
                    "{EventType} | Invite acceptance failed | StoreId={StoreId}",
                    "DELIVERY_DRIVER_INVITE_PASSWORD_MISMATCH",
                    link.StoreId);
                return new AcceptDeliveryDriverInviteResultDto
                {
                    InvalidPassword = true,
                    Error = "Não foi possível aceitar o convite com estas credenciais."
                };
            }

            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }
        }

        var roleResult = await EnsureDeliveryDriverRoleAsync(user);
        if (!roleResult.Succeeded)
        {
            return new AcceptDeliveryDriverInviteResultDto
            {
                Error = string.Join("; ", roleResult.Errors.Select(x => x.Description))
            };
        }

        driver.UserId = user.Id;
        driver.FullName = string.IsNullOrWhiteSpace(request.FullName) ? driver.FullName : request.FullName.Trim();
        driver.PhoneNumber = string.IsNullOrWhiteSpace(user.PhoneNumber) ? driver.PhoneNumber : user.PhoneNumber!;
        driver.WhatsAppPhoneNumber ??= driver.PhoneNumber;
        driver.MarkAsUpdated();

        link.Status = StoreDeliveryDriverStatus.Active;
        link.AcceptedAtUtc = DateTime.UtcNow;
        link.InviteTokenHash = null;
        link.InviteExpiresAtUtc = null;
        link.MarkAsUpdated();

        var storeName = await _dbContext.Stores
            .Where(x => x.Id == link.StoreId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = user.Id,
            Event = "DeliveryDriverInviteAccepted",
            Entity = nameof(StoreDeliveryDriver),
            EntityId = link.Id,
            Description = $"Delivery driver {email} accepted the invite for store {link.StoreId}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AcceptDeliveryDriverInviteResultDto
        {
            Succeeded = true,
            StoreName = storeName
        };
    }

    public async Task<IReadOnlyCollection<DeliveryDriverStoreResponseDto>> ListDriverStoresAsync(
        Guid driverUserId,
        CancellationToken cancellationToken = default)
    {
        var driver = await _dbContext.DeliveryDrivers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == driverUserId, cancellationToken);

        if (driver is null)
        {
            return [];
        }

        var rows = await (
            from link in _dbContext.StoreDeliveryDrivers.AsNoTracking()
            join store in _dbContext.Stores.AsNoTracking() on link.StoreId equals store.Id
            where link.DeliveryDriverId == driver.Id && link.Status == StoreDeliveryDriverStatus.Active
            orderby store.Name
            select new { link, store.Name }
        ).ToListAsync(cancellationToken);

        var storeIds = rows.Select(x => x.link.StoreId).ToArray();
        var activeCounts = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .Where(x => x.DeliveryDriverId == driver.Id && storeIds.Contains(x.StoreId))
            .Join(
                _dbContext.Orders.AsNoTracking().Where(o =>
                    o.Status == OrderStatus.Ready || o.Status == OrderStatus.OnDelivery),
                assignment => assignment.OrderId,
                order => order.Id,
                (assignment, _) => assignment.StoreId)
            .GroupBy(x => x)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);

        return rows.Select(x => new DeliveryDriverStoreResponseDto
        {
            StoreId = x.link.StoreId,
            StoreName = x.Name,
            AcceptedAtUtc = x.link.AcceptedAtUtc,
            AssignedOrderCount = activeCounts.GetValueOrDefault(x.link.StoreId)
        }).ToList();
    }

    public async Task<DriverOrdersResultDto> ListDriverOrdersAsync(
        Guid driverUserId,
        Guid storeId,
        CancellationToken cancellationToken = default)
    {
        var driver = await _dbContext.DeliveryDrivers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == driverUserId, cancellationToken);

        if (driver is null || storeId == Guid.Empty || !await HasActiveLinkAsync(driver.Id, storeId, cancellationToken))
        {
            return new DriverOrdersResultDto { StoreNotAvailable = true };
        }

        var orderIds = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .Where(x => x.DeliveryDriverId == driver.Id && x.StoreId == storeId)
            .Join(
                _dbContext.Orders.AsNoTracking().Where(o =>
                    o.StoreId == storeId
                    && o.FulfillmentType == FulfillmentType.Delivery
                    && o.SellerCompletedAtUtc == null
                    && (o.Status == OrderStatus.Ready
                        || o.Status == OrderStatus.OnDelivery
                        || (o.Status == OrderStatus.Delivered && o.DeliveryPinConfirmedAtUtc == null))),
                assignment => assignment.OrderId,
                order => order.Id,
                (assignment, order) => order.Id)
            .ToListAsync(cancellationToken);

        if (orderIds.Count == 0)
        {
            return new DriverOrdersResultDto();
        }

        return new DriverOrdersResultDto
        {
            Orders = await LoadDriverOrdersAsync(orderIds, cancellationToken)
        };
    }

    public async Task<DriverOrderSummaryResponseDto?> GetDriverOrderAsync(
        Guid driverUserId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var driver = await _dbContext.DeliveryDrivers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == driverUserId, cancellationToken);

        if (driver is null)
        {
            return null;
        }

        var allowed = await (
            from assignment in _dbContext.OrderDeliveryAssignments.AsNoTracking()
            join order in _dbContext.Orders.AsNoTracking() on assignment.OrderId equals order.Id
            join link in _dbContext.StoreDeliveryDrivers.AsNoTracking()
                on new { assignment.StoreId, assignment.DeliveryDriverId }
                equals new { link.StoreId, link.DeliveryDriverId }
            where assignment.OrderId == orderId
                && assignment.DeliveryDriverId == driver.Id
                && link.Status == StoreDeliveryDriverStatus.Active
            select assignment.Id
        ).AnyAsync(cancellationToken);

        if (!allowed)
        {
            return null;
        }

        var orders = await LoadDriverOrdersAsync([orderId], cancellationToken);
        return orders.SingleOrDefault();
    }

    public async Task<DeliveryDriverActionResultDto> StartDeliveryAsync(
        Guid driverUserId,
        Guid orderId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadDriverOrderContextAsync(driverUserId, orderId, cancellationToken);
        if (context is null)
        {
            return new DeliveryDriverActionResultDto { NotFound = true };
        }

        var (order, assignment, driver, link) = context.Value;

        if (link.Status != StoreDeliveryDriverStatus.Active)
        {
            return new DeliveryDriverActionResultDto { Forbidden = true };
        }

        if (order.FulfillmentType != FulfillmentType.Delivery)
        {
            return new DeliveryDriverActionResultDto { InvalidState = true };
        }

        if (order.Status == OrderStatus.OnDelivery)
        {
            return new DeliveryDriverActionResultDto
            {
                Status = order.Status,
                DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc
            };
        }

        if (order.Status != OrderStatus.Ready || order.SellerCompletedAtUtc is not null)
        {
            return new DeliveryDriverActionResultDto { InvalidState = true };
        }

        var previousStatus = order.Status;
        var changedAtUtc = DateTime.UtcNow;
        order.Status = OrderStatus.OnDelivery;
        order.StatusVersion += 1;
        order.MarkAsUpdated();
        assignment.DeliveryStartedAtUtc = changedAtUtc;
        assignment.AdvanceConcurrency();
        assignment.MarkAsUpdated();

        await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
        {
            OrderId = order.Id,
            PreviousStatus = previousStatus,
            NewStatus = OrderStatus.OnDelivery,
            ChangedByUserId = driverUserId,
            Notes = "Delivery driver started the delivery."
        }, cancellationToken);

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = driverUserId,
            Event = "DeliveryStarted",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Delivery driver {driver.FullName} started delivery for order {order.Code}.",
            IpAddress = ipAddress
        }, cancellationToken);

        await _notificationService.NotifyCustomerOrderStatusChangedAsync(
            order.CustomerUserId,
            order.Id,
            OrderStatus.OnDelivery,
            $"Seu pedido {order.Code} saiu para entrega.",
            cancellationToken);

        await _outboxWriter.EnqueueAsync(
            OutboxEventTypes.OrderStatusChanged,
            order.Id,
            new OrderStatusChangedEvent
            {
                OrderId = order.Id,
                StoreId = order.StoreId,
                CustomerUserId = order.CustomerUserId,
                SellerUserId = (await GetStoreOwnerAsync(order.StoreId, cancellationToken)) ?? Guid.Empty,
                Code = order.Code,
                PreviousStatus = previousStatus,
                NewStatus = OrderStatus.OnDelivery,
                ChangedAtUtc = changedAtUtc,
                Source = "Driver",
                Sequence = order.StatusVersion
            },
            changedAtUtc,
            sequence: order.StatusVersion,
            aggregateType: nameof(Order),
            cancellationToken: cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new DeliveryDriverActionResultDto { InvalidState = true };
        }

        return new DeliveryDriverActionResultDto
        {
            Status = order.Status,
            DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc
        };
    }

    public async Task<DeliveryDriverActionResultDto> ConfirmDeliveryPinAsync(
        Guid driverUserId,
        Guid orderId,
        string pin,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadDriverOrderContextAsync(driverUserId, orderId, cancellationToken);
        if (context is null)
        {
            return new DeliveryDriverActionResultDto { NotFound = true };
        }

        var (order, assignment, driver, link) = context.Value;

        if (link.Status != StoreDeliveryDriverStatus.Active)
        {
            return new DeliveryDriverActionResultDto { Forbidden = true };
        }

        if (order.FulfillmentType != FulfillmentType.Delivery)
        {
            return new DeliveryDriverActionResultDto { InvalidState = true };
        }

        if (order.DeliveryConfirmedAtUtc is not null)
        {
            return new DeliveryDriverActionResultDto
            {
                Status = order.Status,
                DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc
            };
        }

        if (order.Status is not (OrderStatus.OnDelivery or OrderStatus.Delivered))
        {
            return new DeliveryDriverActionResultDto { InvalidState = true };
        }

        if (string.IsNullOrWhiteSpace(order.DeliveryPinHash))
        {
            return new DeliveryDriverActionResultDto { PinUnavailable = true };
        }

        var attemptAtUtc = DateTime.UtcNow;
        if (assignment.PinAttemptCount >= MaxPinAttempts)
        {
            var lockExpiresAtUtc = (assignment.LastPinAttemptAtUtc ?? attemptAtUtc).Add(PinLockoutCooldown);
            if (lockExpiresAtUtc > attemptAtUtc)
            {
                return new DeliveryDriverActionResultDto
                {
                    PinLocked = true,
                    RemainingAttempts = 0
                };
            }

            // Cooldown elapsed: the counter is reset so a legitimate delivery is never
            // permanently blocked by five mistyped digits.
            assignment.PinAttemptCount = 0;
            assignment.LastPinAttemptAtUtc = null;
            assignment.MarkAsUpdated();
        }

        var verified = DeliveryPinCode.Verify(order, order.DeliveryPinHash, pin);

        if (!verified)
        {
            // The counter is capped at MaxPinAttempts even if two requests read the same old
            // value. ConcurrencyVersion additionally makes the database reject the stale write,
            // so simultaneous requests cannot overwrite each other's attempts.
            assignment.PinAttemptCount = Math.Min(assignment.PinAttemptCount + 1, MaxPinAttempts);
            assignment.LastPinAttemptAtUtc = attemptAtUtc;
            assignment.AdvanceConcurrency();
            assignment.MarkAsUpdated();

            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                UserId = driverUserId,
                Event = "DeliveryPinRejected",
                Entity = nameof(Order),
                EntityId = order.Id,
                Description = $"Invalid delivery PIN attempt for order {order.Code} (attempt {assignment.PinAttemptCount}).",
                IpAddress = ipAddress
            }, cancellationToken);

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return await ResolvePinConcurrencyAsync(orderId, cancellationToken);
            }

            var remaining = Math.Max(MaxPinAttempts - assignment.PinAttemptCount, 0);
            return new DeliveryDriverActionResultDto
            {
                PinInvalid = remaining > 0,
                PinLocked = remaining == 0,
                RemainingAttempts = remaining,
                Status = order.Status
            };
        }

        var confirmedAtUtc = DateTime.UtcNow;
        var previousStatus = order.Status;
        order.DeliveryConfirmedAtUtc = confirmedAtUtc;
        order.DeliveryPinConfirmedAtUtc = confirmedAtUtc;
        order.DeliveryPinConfirmedByDriverId = driver.Id;

        if (order.Status == OrderStatus.OnDelivery)
        {
            order.Status = OrderStatus.Delivered;
            order.StatusVersion += 1;
        }

        // Bumped on every confirmation path, including an order already marked Delivered by
        // the seller, so two concurrent confirmations cannot both succeed.
        assignment.AdvanceConcurrency();
        assignment.MarkAsUpdated();
        order.MarkAsUpdated();

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = driverUserId,
            Event = "DeliveryPinConfirmed",
            Entity = nameof(Order),
            EntityId = order.Id,
            Description = $"Delivery PIN confirmed by driver {driver.FullName} for order {order.Code}.",
            IpAddress = ipAddress
        }, cancellationToken);

        if (previousStatus != order.Status)
        {
            await _dbContext.OrderStatusHistories.AddAsync(new OrderStatusHistory
            {
                OrderId = order.Id,
                PreviousStatus = previousStatus,
                NewStatus = order.Status,
                ChangedByUserId = driverUserId,
                Notes = "Delivery confirmed with PIN by the delivery driver."
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
                    SellerUserId = (await GetStoreOwnerAsync(order.StoreId, cancellationToken)) ?? Guid.Empty,
                    Code = order.Code,
                    PreviousStatus = previousStatus,
                    NewStatus = order.Status,
                    ChangedAtUtc = confirmedAtUtc,
                    Source = "Driver",
                    Sequence = order.StatusVersion
                },
                confirmedAtUtc,
                sequence: order.StatusVersion,
                aggregateType: nameof(Order),
                cancellationToken: cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolvePinConcurrencyAsync(orderId, cancellationToken);
        }

        return new DeliveryDriverActionResultDto
        {
            Status = order.Status,
            DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc
        };
    }

    /// <summary>
    /// Resolves the race that made a PIN write lose the optimistic concurrency check.
    /// If another request already confirmed the delivery the call is idempotent and reports
    /// success; otherwise the caller must retry with the refreshed attempt counter.
    /// </summary>
    private async Task<DeliveryDriverActionResultDto> ResolvePinConcurrencyAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var state = await _dbContext.Orders
            .AsNoTracking()
            .Where(x => x.Id == orderId)
            .Select(x => new
            {
                x.Status,
                x.DeliveryConfirmedAtUtc,
                Attempt = _dbContext.OrderDeliveryAssignments
                    .Where(a => a.OrderId == x.Id)
                    .Select(a => (int?)a.PinAttemptCount)
                    .FirstOrDefault(),
                LastAttemptAtUtc = _dbContext.OrderDeliveryAssignments
                    .Where(a => a.OrderId == x.Id)
                    .Select(a => a.LastPinAttemptAtUtc)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
        {
            return new DeliveryDriverActionResultDto { NotFound = true };
        }

        if (state.DeliveryConfirmedAtUtc is not null)
        {
            return new DeliveryDriverActionResultDto
            {
                Status = state.Status,
                DeliveryConfirmedAtUtc = state.DeliveryConfirmedAtUtc
            };
        }

        var attemptCount = state.Attempt ?? 0;
        var cooldownExpired = attemptCount >= MaxPinAttempts
            && (state.LastAttemptAtUtc ?? DateTime.UtcNow).Add(PinLockoutCooldown) <= DateTime.UtcNow;
        var effectiveAttemptCount = cooldownExpired ? 0 : attemptCount;
        var locked = attemptCount >= MaxPinAttempts && !cooldownExpired;
        var remaining = locked ? 0 : Math.Max(MaxPinAttempts - effectiveAttemptCount, 0);

        return new DeliveryDriverActionResultDto
        {
            ConcurrentUpdate = true,
            PinLocked = locked,
            PinInvalid = !locked,
            RemainingAttempts = remaining,
            Status = state.Status
        };
    }

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

        if (order.DeliveryPinHash is null)
        {
            // Legacy delivery orders created before the PIN feature get a PIN on first assignment,
            // so the driver workflow works without a data backfill migration.
            var (hash, encrypted) = GenerateDeliveryPin(order);
            order.DeliveryPinHash = hash;
            order.DeliveryPinEncrypted = encrypted;
        }

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

    private async Task IssueInviteAsync(
        StoreDeliveryDriver link,
        Store store,
        DeliveryDriver driver,
        CancellationToken cancellationToken)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        link.InviteTokenHash = HashInviteToken(token);
        link.InviteExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime);
        link.InviteSentAtUtc = DateTime.UtcNow;
        link.InviteVersion += 1;

        var baseUrl = _emailOptions.FrontendBaseUrl.TrimEnd('/');
        var path = _emailOptions.DeliveryInvitePath.Trim('/');
        var inviteUrl = $"{baseUrl}/{path}/{Uri.EscapeDataString(token)}";
        var (subject, body) = EmailTemplates.BuildDeliveryDriverInvite(store.Name, link.DisplayName, inviteUrl);

        await _outboxWriter.EnqueueAsync(
            OutboxEventTypes.OutboundMessageRequested,
            link.Id,
            new OutboundMessageRequestedEvent
            {
                Channel = "Email",
                Recipient = driver.Email ?? string.Empty,
                ToName = link.DisplayName,
                Subject = subject,
                Body = body,
                // Unique per resend so an earlier invite delivery cannot suppress a new one.
                DeliveryKey = $"delivery-invite:{link.Id:N}:{link.InviteVersion}",
                Template = "DeliveryDriverInvite"
            },
            DateTime.UtcNow,
            aggregateType: nameof(StoreDeliveryDriver),
            cancellationToken: cancellationToken);
    }

    private async Task<StoreDeliveryDriver?> FindInviteByTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = HashInviteToken(token);
        return await _dbContext.StoreDeliveryDrivers
            .Include(x => x.DeliveryDriver)
            .SingleOrDefaultAsync(x => x.InviteTokenHash == hash, cancellationToken);
    }

    private async Task<bool> HasActiveLinkAsync(
        Guid driverId,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StoreDeliveryDrivers
            .AsNoTracking()
            .AnyAsync(
                x => x.DeliveryDriverId == driverId
                    && x.StoreId == storeId
                    && x.Status == StoreDeliveryDriverStatus.Active,
                cancellationToken);
    }

    private async Task<(Order Order, OrderDeliveryAssignment Assignment, DeliveryDriver Driver, StoreDeliveryDriver Link)?>
        LoadDriverOrderContextAsync(Guid driverUserId, Guid orderId, CancellationToken cancellationToken)
    {
        var driver = await _dbContext.DeliveryDrivers
            .SingleOrDefaultAsync(x => x.UserId == driverUserId, cancellationToken);

        if (driver is null)
        {
            return null;
        }

        var order = await _dbContext.Orders
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var assignment = await _dbContext.OrderDeliveryAssignments
            .SingleOrDefaultAsync(
                x => x.OrderId == orderId
                    && x.DeliveryDriverId == driver.Id
                    && x.StoreId == order.StoreId,
                cancellationToken);

        if (assignment is null)
        {
            return null;
        }

        var link = await _dbContext.StoreDeliveryDrivers
            .SingleOrDefaultAsync(
                x => x.StoreId == order.StoreId && x.DeliveryDriverId == driver.Id,
                cancellationToken);

        if (link is null)
        {
            return null;
        }

        return (order, assignment, driver, link);
    }

    private async Task<IReadOnlyCollection<DriverOrderSummaryResponseDto>> LoadDriverOrdersAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.Id))
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var customerIds = orders.Select(x => x.CustomerUserId).Distinct().ToArray();
        var phones = await _dbContext.Users
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PhoneNumber })
            .ToDictionaryAsync(x => x.Id, x => x.PhoneNumber, cancellationToken);
        var names = await _dbContext.UserClaims
            .AsNoTracking()
            .Where(x => customerIds.Contains(x.UserId) && x.ClaimType == "FullName")
            .Select(x => new { x.UserId, x.ClaimValue })
            .ToDictionaryAsync(x => x.UserId, x => x.ClaimValue, cancellationToken);
        var items = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var assignments = await _dbContext.OrderDeliveryAssignments
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .ToDictionaryAsync(x => x.OrderId, cancellationToken);

        // Origin comes from the persisted store address. Destination comes from the order
        // snapshot captured at checkout, never from the current customer address: editing the
        // address after checkout must not move an order that is already in progress.
        // Missing coordinates stay null so the portal can ask for external navigation instead.
        var storeIds = orders.Select(x => x.StoreId).Distinct().ToArray();
        var storeCoordinates = (await _dbContext.StoreAddresses
                .AsNoTracking()
                .Where(x => storeIds.Contains(x.StoreId) && x.Latitude != null && x.Longitude != null)
                .Select(x => new { x.StoreId, Latitude = x.Latitude!.Value, Longitude = x.Longitude!.Value })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.StoreId)
            .ToDictionary(x => x.Key, x => (x.First().Latitude, x.First().Longitude));

        return orders.Select(order =>
        {
            assignments.TryGetValue(order.Id, out var assignment);
            (double Latitude, double Longitude)? origin = null;
            if (storeCoordinates.TryGetValue(order.StoreId, out var storeCoordinate))
            {
                origin = storeCoordinate;
            }

            (double Latitude, double Longitude)? destination = null;
            if (order.DeliveryLatitude is { } latitude && order.DeliveryLongitude is { } longitude)
            {
                destination = (latitude, longitude);
            }

            return new DriverOrderSummaryResponseDto
            {
                Id = order.Id,
                Code = order.Code,
                StoreId = order.StoreId,
                Status = order.Status,
                FulfillmentType = order.FulfillmentType,
                PaymentMethod = order.PaymentMethod,
                CustomerName = names.GetValueOrDefault(order.CustomerUserId),
                CustomerPhoneNumber = phones.GetValueOrDefault(order.CustomerUserId),
                AddressCep = order.AddressCep,
                AddressStreet = order.AddressStreet,
                AddressNumber = order.AddressNumber,
                AddressNeighborhood = order.AddressNeighborhood,
                AddressCity = order.AddressCity,
                AddressState = order.AddressState,
                AddressComplement = order.AddressComplement,
                AddressReference = order.AddressReference,
                AddressSummary = BuildAddressSummary(order),
                OriginLatitude = origin?.Latitude,
                OriginLongitude = origin?.Longitude,
                DestinationLatitude = destination?.Latitude,
                DestinationLongitude = destination?.Longitude,
                Notes = order.Notes,
                Subtotal = order.Subtotal,
                DeliveryFee = order.DeliveryFee,
                Total = order.Total,
                CreatedAtUtc = order.CreatedAtUtc,
                DeliveryStartedAtUtc = assignment?.DeliveryStartedAtUtc,
                DeliveryConfirmedAtUtc = order.DeliveryConfirmedAtUtc,
                Items = items
                    .Where(x => x.OrderId == order.Id)
                    .Select(x => new OrderItemResponseDto
                    {
                        ProductName = x.ProductName,
                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        TotalPrice = x.TotalPrice,
                        Notes = x.Notes,
                        VariationName = x.VariationName,
                        WeightGrams = x.WeightGrams,
                        ChoiceOptionName = x.ChoiceOptionName,
                        AdditionalNames = x.AdditionalNames
                    })
                    .ToList(),
                CanStartDelivery = order.Status == OrderStatus.Ready && assignment?.DeliveryStartedAtUtc is null,
                CanConfirmPin = order.DeliveryConfirmedAtUtc is null
                    && order.Status is OrderStatus.OnDelivery or OrderStatus.Delivered
            };
        }).ToList();
    }

    private async Task<Guid?> GetStoreOwnerAsync(Guid storeId, CancellationToken cancellationToken)
    {
        return await _dbContext.Stores
            .AsNoTracking()
            .Where(x => x.Id == storeId)
            .Select(x => (Guid?)x.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<IdentityResult> EnsureDeliveryDriverRoleAsync(IdentityUser<Guid> user)
    {
        if (!await _roleManager.RoleExistsAsync(DeliveryDriverRole))
        {
            var createRole = await _roleManager.CreateAsync(new IdentityRole<Guid>(DeliveryDriverRole));
            if (!createRole.Succeeded)
            {
                return createRole;
            }
        }

        if (await _userManager.IsInRoleAsync(user, DeliveryDriverRole))
        {
            return IdentityResult.Success;
        }

        return await _userManager.AddToRoleAsync(user, DeliveryDriverRole);
    }

    private static SellerDeliveryDriverResponseDto MapSellerDriver(StoreDeliveryDriver link, DeliveryDriver driver)
    {
        return new SellerDeliveryDriverResponseDto
        {
            Id = link.Id,
            DeliveryDriverId = driver.Id,
            Name = string.IsNullOrWhiteSpace(link.DisplayName) ? driver.FullName : link.DisplayName,
            Email = driver.Email ?? string.Empty,
            PhoneNumber = string.IsNullOrWhiteSpace(link.PhoneNumber) ? driver.PhoneNumber : link.PhoneNumber,
            WhatsAppPhoneNumber = string.IsNullOrWhiteSpace(link.WhatsAppPhoneNumber)
                ? driver.WhatsAppPhoneNumber ?? (string.IsNullOrWhiteSpace(link.PhoneNumber) ? driver.PhoneNumber : link.PhoneNumber)
                : link.WhatsAppPhoneNumber,
            IsActive = link.Status == StoreDeliveryDriverStatus.Active,
            HasAccount = driver.UserId is not null,
            Status = link.Status,
            InviteSentAtUtc = link.InviteSentAtUtc,
            InviteExpiresAtUtc = link.InviteExpiresAtUtc,
            AcceptedAtUtc = link.AcceptedAtUtc,
            RevokedAtUtc = link.RevokedAtUtc,
            CanResendInvite = link.Status == StoreDeliveryDriverStatus.Pending && driver.Email is not null
        };
    }

    internal (string Hash, string Encrypted) GenerateDeliveryPin(Order order)
    {
        var pin = DeliveryPinCode.Generate();
        var hash = DeliveryPinCode.Hash(order, pin);
        return (hash, _encryptionService.Encrypt(pin));
    }

    private static string HashInviteToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "***";
        }

        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var local = email[..at];
        var domain = email[(at + 1)..];
        var visible = local.Length <= 2 ? local[..1] : local[..2];
        return $"{visible}***@{domain}";
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
