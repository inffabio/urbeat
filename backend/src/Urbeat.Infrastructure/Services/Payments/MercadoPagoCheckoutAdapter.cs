using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.Payments;

public sealed class MercadoPagoCheckoutAdapter : IMercadoPagoCheckoutAdapter
{
    private readonly HttpClient _httpClient;
    private readonly MercadoPagoOptions _options;
    private readonly IEncryptionService _encryptionService;
    private readonly Urbeat.Infrastructure.Persistence.ApplicationDbContext _dbContext;

    public MercadoPagoCheckoutAdapter(
        HttpClient httpClient,
        IOptions<MercadoPagoOptions> options,
        IEncryptionService encryptionService,
        Urbeat.Infrastructure.Persistence.ApplicationDbContext dbContext)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _encryptionService = encryptionService;
        _dbContext = dbContext;
    }

    private async Task<string> ResolveAccessTokenAsync(Guid? storeId)
    {
        if (storeId.HasValue)
        {
            var config = await _dbContext.StorePaymentGatewayConfigs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.StoreId == storeId.Value && x.Gateway == PaymentGateway.MercadoPago && x.IsActive);

            if (config is not null && !string.IsNullOrWhiteSpace(config.EncryptedAccessToken))
            {
                return _encryptionService.Decrypt(config.EncryptedAccessToken);
            }
        }

        return _options.AccessToken;
    }

    private async Task<string?> ResolveNotificationUrlAsync(Guid? storeId)
    {
        if (storeId.HasValue)
        {
            var config = await _dbContext.StorePaymentGatewayConfigs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.StoreId == storeId.Value && x.Gateway == PaymentGateway.MercadoPago && x.IsActive);

            if (config is not null && !string.IsNullOrWhiteSpace(config.EncryptedNotificationUrl))
            {
                return _encryptionService.Decrypt(config.EncryptedNotificationUrl);
            }
        }

        return _options.NotificationUrl;
    }

    public async Task<MercadoPagoCheckoutCreateResponse> CreateCheckoutAsync(
        MercadoPagoCheckoutCreateRequest request,
        Guid? storeId = null,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await ResolveAccessTokenAsync(storeId);
        var notificationUrl = await ResolveNotificationUrlAsync(storeId);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var fakeId = $"pref_{Guid.NewGuid():N}";
            var fakePayload = JsonSerializer.Serialize(new
            {
                id = fakeId,
                init_point = $"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id={fakeId}",
                sandbox_init_point = $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id={fakeId}",
                external_reference = request.ExternalReference,
                mode = "fake"
            });

            return new MercadoPagoCheckoutCreateResponse
            {
                TransactionId = fakeId,
                CheckoutUrl = $"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id={fakeId}",
                RawPayload = fakePayload
            };
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.PostAsJsonAsync("/checkout/preferences", new
        {
            external_reference = request.ExternalReference,
            payer = new
            {
                email = request.PayerEmail
            },
            items = request.Items.Select(x => new
            {
                title = x.Title,
                quantity = x.Quantity,
                currency_id = "BRL",
                unit_price = x.UnitPrice
            }),
            notification_url = notificationUrl,
            back_urls = new
            {
                success = _options.SuccessUrl,
                failure = _options.FailureUrl,
                pending = _options.PendingUrl
            },
            auto_return = "approved"
        }, cancellationToken);

        var rawPayload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Mercado Pago checkout failed: {response.StatusCode} - {rawPayload}");
        }

        var parsedPayload = JsonSerializer.Deserialize<MercadoPagoPreferenceResponse>(rawPayload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Mercado Pago response could not be parsed.");

        if (string.IsNullOrWhiteSpace(parsedPayload.Id) || string.IsNullOrWhiteSpace(parsedPayload.InitPoint))
        {
            throw new InvalidOperationException("Mercado Pago response missing required fields.");
        }

        return new MercadoPagoCheckoutCreateResponse
        {
            TransactionId = parsedPayload.Id,
            CheckoutUrl = parsedPayload.InitPoint,
            RawPayload = rawPayload
        };
    }

    public async Task<MercadoPagoPaymentDetails> GetPaymentDetailsAsync(
        string transactionId,
        Guid? storeId = null,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await ResolveAccessTokenAsync(storeId);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var fakePayload = JsonSerializer.Serialize(new
            {
                id = transactionId,
                status = "approved",
                mode = "fake"
            });

            return new MercadoPagoPaymentDetails
            {
                TransactionId = transactionId,
                Status = "approved",
                RawPayload = fakePayload
            };
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.GetAsync($"/v1/payments/{transactionId}", cancellationToken);
        var rawPayload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Mercado Pago payment details failed: {response.StatusCode} - {rawPayload}");
        }

        var payload = JsonSerializer.Deserialize<MercadoPagoPaymentLookupResponse>(rawPayload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Mercado Pago payment response could not be parsed.");

        if (string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Status))
        {
            throw new InvalidOperationException("Mercado Pago payment response missing required fields.");
        }

        return new MercadoPagoPaymentDetails
        {
            TransactionId = payload.Id,
            Status = payload.Status,
            RawPayload = rawPayload
        };
    }

    private sealed class MercadoPagoPreferenceResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("init_point")]
        public string? InitPoint { get; init; }
    }

    private sealed class MercadoPagoPaymentLookupResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
