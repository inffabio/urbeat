namespace Urbeat.Infrastructure.Services.Payments;

public interface IMercadoPagoCheckoutAdapter
{
    Task<MercadoPagoCheckoutCreateResponse> CreateCheckoutAsync(
        MercadoPagoCheckoutCreateRequest request,
        Guid? storeId = null,
        CancellationToken cancellationToken = default);

    Task<MercadoPagoPaymentDetails> GetPaymentDetailsAsync(
        string transactionId,
        Guid? storeId = null,
        CancellationToken cancellationToken = default);
}
