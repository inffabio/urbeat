using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreAnalyticsFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly AnalyticsTestData _data;

    public StoreAnalyticsFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _data = new AnalyticsTestData(factory);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldReturnUnauthorized_WithoutToken()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders/store/analytics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldForbidCustomerToken()
    {
        var client = _factory.CreateClient();
        var customerToken = await RegisterAndLoginCustomerAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var response = await client.GetAsync("/api/orders/store/analytics");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldReturnContract_ForSeller()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15),
            subtotal: 80m, deliveryFee: 5m, items: ("X-Burger", 2, 40m));

        var response = await client.GetAsync("/api/orders/store/analytics?period=day&anchor=2026-10-29");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StoreAnalyticsResponseDto>();
        payload.Should().NotBeNull();
        payload!.Period.Should().Be("day");
        payload.AnchorDate.Should().Be(new DateOnly(2026, 10, 29));
        payload.WeekBlock.Should().BeNull();
        payload.TimeZone.Should().Be("America/Sao_Paulo");
        payload.RangeStartUtc.Should().Be(new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc));
        payload.RangeEndUtcExclusive.Should().Be(new DateTime(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc));
        payload.Kpis.DeliveredOrders.Should().Be(1);
        payload.Kpis.Revenue.Should().Be(85m);
        payload.Kpis.DeliveryFees.Should().Be(5m);
        payload.Products.Should().ContainSingle(x => x.ProductName == "X-Burger");
        payload.Months.Should().HaveCount(12);
        payload.AvailableYears.Should().Contain(2026);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldReturnValidationErrors()
    {
        var client = _factory.CreateClient();
        await _data.CreateSellerWithStoreAsync(client);

        var invalidPeriod = await client.GetAsync("/api/orders/store/analytics?period=quarter");
        var invalidAnchor = await client.GetAsync("/api/orders/store/analytics?anchor=2026-02-30");
        var invalidBlock = await client.GetAsync("/api/orders/store/analytics?period=week&weekBlock=5&anchor=2026-02-10");

        invalidPeriod.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidAnchor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidBlock.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await invalidPeriod.Content.ReadFromJsonAsync<ValidationProblemDetails>())!
            .Errors.Should().ContainKey("period");
        (await invalidAnchor.Content.ReadFromJsonAsync<ValidationProblemDetails>())!
            .Errors.Should().ContainKey("anchor");
        (await invalidBlock.Content.ReadFromJsonAsync<ValidationProblemDetails>())!
            .Errors.Should().ContainKey("weekBlock");
    }

    [Fact]
    public async Task StoreAnalytics_ShouldIsolateSellers()
    {
        var clientA = _factory.CreateClient();
        var (_, storeA, _) = await _data.CreateSellerWithStoreAsync(clientA);
        await _data.AddOrderAsync(storeA, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15), subtotal: 10m);

        var clientB = _factory.CreateClient();
        var (_, storeB, _) = await _data.CreateSellerWithStoreAsync(clientB);
        await _data.AddOrderAsync(storeB, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 16), subtotal: 99m);

        var payloadA = await clientA.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=day&anchor=2026-10-29");
        var payloadB = await clientB.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=day&anchor=2026-10-29");

        payloadA!.Kpis.DeliveredOrders.Should().Be(1);
        payloadA.Kpis.Revenue.Should().Be(10m);
        payloadB!.Kpis.DeliveredOrders.Should().Be(1);
        payloadB.Kpis.Revenue.Should().Be(99m);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldOnlyCountDeliveredOrdersInsideTheRange()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15), subtotal: 10m);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 30, 3), subtotal: 20m);
        await _data.AddOrderAsync(storeId, OrderStatus.Cancelled, AnalyticsTestData.AtUtc(2026, 10, 29, 16), subtotal: 30m);

        var payload = await client.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=day&anchor=2026-10-29");

        payload!.Kpis.DeliveredOrders.Should().Be(1);
        payload.Kpis.Revenue.Should().Be(10m);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldEchoRangesForEveryPeriod()
    {
        var client = _factory.CreateClient();
        await _data.CreateSellerWithStoreAsync(client);

        var day = await client.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=day&anchor=2026-10-29");
        var month = await client.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=month&anchor=2026-10-01");
        var year = await client.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=year&anchor=2026-01-01");
        var week = await client.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=week&weekBlock=5&anchor=2026-10-29");

        day!.RangeStartUtc.Should().Be(new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc));
        day.RangeEndUtcExclusive.Should().Be(new DateTime(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc));
        month!.RangeStartUtc.Should().Be(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc));
        month.RangeEndUtcExclusive.Should().Be(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc));
        year!.RangeStartUtc.Should().Be(new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc));
        year.RangeEndUtcExclusive.Should().Be(new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc));
        week!.RangeStartUtc.Should().Be(new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc));
        week.RangeEndUtcExclusive.Should().Be(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc));
        week.AnchorDate.Should().Be(new DateOnly(2026, 10, 29));
        week.WeekBlock.Should().Be(5);
    }

    [Fact]
    public async Task StoreAnalytics_ShouldReturnZeroedResponse_WhenSellerHasNoStore()
    {
        var client = _factory.CreateClient();
        await _data.CreateSellerWithoutStoreAsync(client);
        var currentYear = SaoPauloCalendar.GetLocalDate(TimeProvider.System.GetUtcNow()).Year;

        var response = await client.GetAsync("/api/orders/store/analytics?period=day&anchor=2026-10-29");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<StoreAnalyticsResponseDto>();
        payload!.Kpis.DeliveredOrders.Should().Be(0);
        payload.Kpis.Revenue.Should().Be(0m);
        payload.Products.Should().BeEmpty();
        payload.Months.Should().HaveCount(12);
        payload.AvailableYears.Should().Equal(currentYear);
    }

    [Fact]
    public async Task StoreOrders_ShouldSupportExclusiveEndAndRejectAmbiguousEndBounds()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        var start = AnalyticsTestData.AtUtc(2026, 10, 29, 3);
        var endExclusive = AnalyticsTestData.AtUtc(2026, 10, 30, 3);

        await _data.AddOrderAsync(storeId, OrderStatus.Received, start);
        await _data.AddOrderAsync(storeId, OrderStatus.Received, endExclusive.AddTicks(-1));
        await _data.AddOrderAsync(storeId, OrderStatus.Received, endExclusive);

        var response = await client.GetAsync(
            $"/api/orders/store?startDateUtc={Uri.EscapeDataString(start.ToString("O"))}" +
            $"&endExclusiveUtc={Uri.EscapeDataString(endExclusive.ToString("O"))}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedOrderSummaryResponseDto>();
        page!.TotalItems.Should().Be(2);
        page.Items.Should().NotContain(item => item.CreatedAtUtc == endExclusive);

        var ambiguous = await client.GetAsync(
            $"/api/orders/store?startDateUtc={Uri.EscapeDataString(start.ToString("O"))}" +
            $"&endDateUtc={Uri.EscapeDataString(endExclusive.ToString("O"))}" +
            $"&endExclusiveUtc={Uri.EscapeDataString(endExclusive.ToString("O"))}");

        ambiguous.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"analytics.customer.{Guid.NewGuid():N}@urbeat.local";
        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Analytics Customer",
            Email = email,
            Password = "SenhaForte123",
            PhoneNumber = "11981118888"
        });
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/customer", new LoginRequestDto
        {
            Email = email,
            Password = "SenhaForte123"
        });
        loginResponse.EnsureSuccessStatusCode();
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        return token!.AccessToken;
    }
}
