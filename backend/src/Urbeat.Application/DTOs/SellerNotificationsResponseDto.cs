namespace Urbeat.Application.DTOs;

public sealed class SellerNotificationsResponseDto
{
    public int UnreadCount { get; init; }

    public IReadOnlyCollection<SellerNotificationResponseDto> Items { get; init; } = [];
}
