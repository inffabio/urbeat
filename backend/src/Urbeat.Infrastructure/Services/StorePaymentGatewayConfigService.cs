using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class StorePaymentGatewayConfigService : IStorePaymentGatewayConfigService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEncryptionService _encryptionService;
    private readonly IMapper _mapper;
    private readonly IEfUnitOfWork _efUnitOfWork;

    public StorePaymentGatewayConfigService(
        ApplicationDbContext dbContext,
        IEncryptionService encryptionService,
        IMapper mapper,
        IEfUnitOfWork efUnitOfWork)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
        _mapper = mapper;
        _efUnitOfWork = efUnitOfWork;
    }

    public async Task<PaymentGatewayConfigResponseDto?> GetByStoreAsync(
        Guid ownerUserId, Guid storeId, PaymentGateway gateway, CancellationToken cancellationToken = default)
    {
        var storeOwned = await _dbContext.Stores
            .AsNoTracking()
            .AnyAsync(x => x.Id == storeId && x.OwnerUserId == ownerUserId, cancellationToken);

        if (!storeOwned)
            return null;

        var config = await _dbContext.StorePaymentGatewayConfigs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.Gateway == gateway, cancellationToken);

        if (config is null)
        {
            return new PaymentGatewayConfigResponseDto
            {
                StoreId = storeId,
                Gateway = gateway,
                HasAccessToken = false,
                HasNotificationUrl = false,
                Environment = "Sandbox",
                IsActive = false
            };
        }

        return new PaymentGatewayConfigResponseDto
        {
            StoreId = config.StoreId,
            Gateway = config.Gateway,
            HasAccessToken = !string.IsNullOrWhiteSpace(config.EncryptedAccessToken),
            HasNotificationUrl = !string.IsNullOrWhiteSpace(config.EncryptedNotificationUrl),
            Environment = config.Environment,
            IsActive = config.IsActive
        };
    }

    public async Task<UpsertPaymentGatewayConfigResultDto> UpsertAsync(
        Guid ownerUserId, Guid storeId, UpsertPaymentGatewayConfigRequestDto request,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null)
        {
            return new UpsertPaymentGatewayConfigResultDto { NotFound = true };
        }

        if (store.OwnerUserId != ownerUserId)
        {
            return new UpsertPaymentGatewayConfigResultDto { Forbidden = true };
        }

        var config = await _dbContext.StorePaymentGatewayConfigs
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.Gateway == request.Gateway, cancellationToken);

        if (config is null)
        {
            config = new StorePaymentGatewayConfig
            {
                StoreId = storeId,
                Gateway = request.Gateway,
                EncryptedAccessToken = _encryptionService.Encrypt(request.AccessToken),
                EncryptedNotificationUrl = string.IsNullOrWhiteSpace(request.NotificationUrl)
                    ? null
                    : _encryptionService.Encrypt(request.NotificationUrl),
                Environment = request.Environment,
                IsActive = request.IsActive
            };
            await _dbContext.StorePaymentGatewayConfigs.AddAsync(config, cancellationToken);
        }
        else
        {
            config.EncryptedAccessToken = _encryptionService.Encrypt(request.AccessToken);
            config.EncryptedNotificationUrl = string.IsNullOrWhiteSpace(request.NotificationUrl)
                ? null
                : _encryptionService.Encrypt(request.NotificationUrl);
            config.Environment = request.Environment;
            config.IsActive = request.IsActive;
            config.MarkAsUpdated();
        }

        await WriteAuditLogAsync(ownerUserId, "PaymentGatewayConfigUpserted",
            nameof(StorePaymentGatewayConfig), config.Id,
            $"Payment gateway config upserted for store {storeId}.", ipAddress, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return new UpsertPaymentGatewayConfigResultDto
        {
            Config = new PaymentGatewayConfigResponseDto
            {
                StoreId = config.StoreId,
                Gateway = config.Gateway,
                HasAccessToken = !string.IsNullOrWhiteSpace(config.EncryptedAccessToken),
                HasNotificationUrl = !string.IsNullOrWhiteSpace(config.EncryptedNotificationUrl),
                Environment = config.Environment,
                IsActive = config.IsActive
            }
        };
    }

    public async Task<bool> DeleteAsync(
        Guid ownerUserId, Guid storeId, PaymentGateway gateway,
        string? ipAddress, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(x => x.Id == storeId, cancellationToken);

        if (store is null || store.OwnerUserId != ownerUserId)
            return false;

        var config = await _dbContext.StorePaymentGatewayConfigs
            .SingleOrDefaultAsync(x => x.StoreId == storeId && x.Gateway == gateway, cancellationToken);

        if (config is null)
            return false;

        _dbContext.StorePaymentGatewayConfigs.Remove(config);

        await WriteAuditLogAsync(ownerUserId, "PaymentGatewayConfigDeleted",
            nameof(StorePaymentGatewayConfig), config.Id,
            $"Payment gateway config deleted for store {storeId}.", ipAddress, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task WriteAuditLogAsync(
        Guid userId, string auditEvent, string entity, Guid entityId,
        string description, string? ipAddress, CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress
        }, cancellationToken);
    }
}
