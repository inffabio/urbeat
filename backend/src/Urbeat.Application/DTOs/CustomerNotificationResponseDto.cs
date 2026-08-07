using Urbeat.Domain.Entities;

namespace Urbeat.Application.DTOs;

public sealed class CustomerNotificationResponseDto
{
    public Guid Id { get; init; }

    public Guid OrderId { get; init; }

    public NotificationType Type { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public bool IsRead { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}
