using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed partial class NotificationService : INotificationService
{
        public async Task<CustomerNotificationsResponseDto> ListCustomerNotificationsAsync(
            Guid customerUserId,
            CancellationToken cancellationToken = default)
        {
            var notifications = await _dbContext.Notifications
                .AsNoTracking()
                .Where(x => x.RecipientUserId == customerUserId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(50)
                .Select(x => new CustomerNotificationResponseDto
                {
                    Id = x.Id,
                    OrderId = x.OrderId,
                    Type = x.Type,
                    Title = x.Title,
                    Message = x.Message,
                    IsRead = x.IsRead,
                    CreatedAtUtc = x.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            return new CustomerNotificationsResponseDto
            {
                UnreadCount = notifications.Count(x => !x.IsRead),
                Items = notifications
            };
        }

    private readonly ApplicationDbContext _dbContext;
    private readonly dynamic? _sellerHub;
    private readonly dynamic? _customerHub;

    public NotificationService(ApplicationDbContext dbContext, object? sellerHub = null, object? customerHub = null)
    {
        _dbContext = dbContext;
        _sellerHub = sellerHub;
        _customerHub = customerHub;
    }

    public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.Notifications
            .SingleOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == recipientUserId, cancellationToken);

        if (notification is null) return false;

        notification.IsRead = true;
        notification.ReadAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SellerNotificationsResponseDto> ListSellerNotificationsAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var notifications = await _dbContext.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == sellerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .Select(x => new SellerNotificationResponseDto
            {
                Id = x.Id,
                OrderId = x.OrderId,
                Type = x.Type,
                Title = x.Title,
                Message = x.Message,
                IsRead = x.IsRead,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new SellerNotificationsResponseDto
        {
            UnreadCount = notifications.Count(x => !x.IsRead),
            Items = notifications
        };
    }
}
