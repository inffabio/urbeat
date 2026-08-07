namespace Urbeat.Application.DTOs;

public sealed class CustomerNotificationsResponseDto
{
    public int UnreadCount { get; init; }

    public IReadOnlyCollection<CustomerNotificationResponseDto> Items { get; init; } = [];
}
