using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services;

public sealed class AsaasSubscriptionAdapter : IAsaasSubscriptionAdapter
{
    private readonly HttpClient _httpClient;
    private readonly AsaasSubscriptionOptions _options;

    public AsaasSubscriptionAdapter(HttpClient httpClient, IOptions<AsaasSubscriptionOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<AsaasSubscriptionContractResponse> CreateContractAsync(
        AsaasSubscriptionContractRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var fakeCustomerId = $"cus_{Guid.NewGuid():N}";
            var fakeSubscriptionId = $"sub_{Guid.NewGuid():N}";
            var nextDueDateUtc = request.FirstDueDateUtc.ToUniversalTime();

            var fakePayload = JsonSerializer.Serialize(new
            {
                customer = new { id = fakeCustomerId },
                subscription = new { id = fakeSubscriptionId, nextDueDate = nextDueDateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                mode = "fake"
            });

            return new AsaasSubscriptionContractResponse
            {
                GatewayCustomerId = fakeCustomerId,
                GatewaySubscriptionId = fakeSubscriptionId,
                NextDueDateUtc = nextDueDateUtc,
                RawPayload = fakePayload
            };
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var customerResponse = await _httpClient.PostAsJsonAsync("/v3/customers", new
        {
            name = request.SellerName,
            email = request.SellerEmail,
            phone = request.SellerPhone,
            externalReference = request.ExternalReference
        }, cancellationToken);

        customerResponse.EnsureSuccessStatusCode();
        var customerRawPayload = await customerResponse.Content.ReadAsStringAsync(cancellationToken);

        var customer = JsonSerializer.Deserialize<AsaasCustomerResponse>(customerRawPayload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Asaas customer response could not be parsed.");

        if (string.IsNullOrWhiteSpace(customer.Id))
        {
            throw new InvalidOperationException("Asaas customer response missing id.");
        }

        var dueDate = request.FirstDueDateUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var subscriptionResponse = await _httpClient.PostAsJsonAsync("/v3/subscriptions", new
        {
            customer = customer.Id,
            billingType = "UNDEFINED",
            value = request.PlanAmount,
            nextDueDate = dueDate,
            cycle = "MONTHLY",
            description = "Assinatura Urbeat",
            externalReference = request.ExternalReference
        }, cancellationToken);

        subscriptionResponse.EnsureSuccessStatusCode();
        var subscriptionRawPayload = await subscriptionResponse.Content.ReadAsStringAsync(cancellationToken);

        var subscription = JsonSerializer.Deserialize<AsaasSubscriptionResponse>(subscriptionRawPayload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Asaas subscription response could not be parsed.");

        if (string.IsNullOrWhiteSpace(subscription.Id) || string.IsNullOrWhiteSpace(subscription.NextDueDate))
        {
            throw new InvalidOperationException("Asaas subscription response missing required fields.");
        }

        if (!DateTime.TryParse(subscription.NextDueDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var nextDueDateParsed))
        {
            throw new InvalidOperationException("Asaas subscription nextDueDate is invalid.");
        }

        return new AsaasSubscriptionContractResponse
        {
            GatewayCustomerId = customer.Id,
            GatewaySubscriptionId = subscription.Id,
            NextDueDateUtc = nextDueDateParsed.ToUniversalTime(),
            RawPayload = $"{{\"customer\":{customerRawPayload},\"subscription\":{subscriptionRawPayload}}}"
        };
    }

    private sealed class AsaasCustomerResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed class AsaasSubscriptionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("nextDueDate")]
        public string? NextDueDate { get; init; }
    }
}
