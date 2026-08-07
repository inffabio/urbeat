using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

public interface IOrderService
{
    Task<IReadOnlyCollection<OrderSummaryResponseDto>> ListCustomerOrdersAsync(Guid customerUserId, CancellationToken cancellationToken = default);

    Task<OrderDetailsResponseDto?> GetCustomerOrderAsync(Guid customerUserId, Guid orderId, CancellationToken cancellationToken = default);

    Task<PagedOrderSummaryResponseDto> ListStoreOrdersAsync(
        Guid sellerUserId,
        StoreOrdersHistoryQueryDto query,
        CancellationToken cancellationToken = default);

    Task<OrderDetailsResponseDto?> GetStoreOrderAsync(Guid sellerUserId, Guid orderId, CancellationToken cancellationToken = default);

    Task<PagedSellerCustomerSummaryResponseDto> ListStoreCustomersAsync(
        Guid sellerUserId,
        StoreCustomersQueryDto query,
        CancellationToken cancellationToken = default);

    Task<UpdateStoreCustomerResultDto> UpdateStoreCustomerAsync(Guid sellerUserId, Guid customerUserId, UpdateStoreCustomerRequestDto request, CancellationToken cancellationToken = default);
    Task<UpdateStoreCustomerResultDto> UpdateStoreCustomerStatusAsync(Guid sellerUserId, Guid customerUserId, bool isActive, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SellerDeliverySummaryResponseDto>> ListStoreDeliveriesAsync(Guid sellerUserId, CancellationToken cancellationToken = default);

    Task<UpdateOrderStatusResultDto> UpdateStatusAsync(
        Guid sellerUserId,
        Guid orderId,
        UpdateOrderStatusRequestDto request,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
