using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreSalesFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly AnalyticsTestData _data;

    public StoreSalesFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _data = new AnalyticsTestData(factory);
    }

    [Fact]
    public async Task StoreSales_ShouldReturnUnauthorizedWithoutToken_AndForbidCustomerToken()
    {
        var anonymousClient = _factory.CreateClient();
        var anonymousResponse = await anonymousClient.GetAsync("/api/orders/store/sales");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var customerClient = _factory.CreateClient();
        var customerToken = await RegisterAndLoginCustomerAsync(customerClient);
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        var customerResponse = await customerClient.GetAsync("/api/orders/store/sales");
        customerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StoreSales_ShouldReturnPagedContract_ForSeller()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15),
            subtotal: 80m, deliveryFee: 5m, customerName: "Maria Silva",
            items: [("X-Burger", 2, 35m), ("Batata grande", 1, 10m)]);

        var response = await client.GetAsync(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PagedStoreSalesResponseDto>();
        payload.Should().NotBeNull();
        payload!.Page.Should().Be(1);
        payload.PageSize.Should().Be(20);
        payload.TotalItems.Should().Be(1);
        payload.TotalPages.Should().Be(1);
        payload.Totals.SalesTotal.Should().Be(85m);
        payload.Totals.DeliveryFees.Should().Be(5m);
        payload.Sales.Should().ContainSingle();
        var sale = payload.Sales.Single();
        sale.Code.Should().NotBeNullOrWhiteSpace();
        sale.CustomerName.Should().Be("Maria Silva");
        sale.Subtotal.Should().Be(80m);
        sale.DeliveryFee.Should().Be(5m);
        sale.Total.Should().Be(85m);
        sale.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task StoreSales_ShouldExposeOnlyCustomerNameAsPii()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15),
            customerName: "Maria Silva",
            addressStreet: "Rua Secreta 123",
            notes: "Sem cebola",
            items: ("X-Burger", 1, 10m));

        var response = await client.GetAsync("/api/orders/store/sales?period=day&anchor=2026-10-29");
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().Contain("customerName");
        raw.Should().Contain("Maria Silva");
        raw.Should().NotContain("Rua Secreta 123");
        raw.Should().NotContain("Sem cebola");
        foreach (var forbidden in new[]
        {
            "customerPhoneNumber", "addressSummary", "itemsSummary", "notes",
            "deliveryPin", "deliveryRouteId", "customerUserId", "customerEmail"
        })
        {
            raw.Should().NotContain($"\"{forbidden}\"");
        }
    }

    [Fact]
    public async Task StoreSales_ShouldReturnOnlyTheRequestedPageAndItsItems()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 10),
            subtotal: 10m, items: ("Primeiro", 1, 10m));
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 11),
            subtotal: 20m, items: ("Segundo", 2, 10m));
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 12),
            subtotal: 30m, items: ("Terceiro", 3, 10m));

        var pageTwo = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=2&pageSize=1");

        pageTwo!.Sales.Should().ContainSingle();
        pageTwo.Sales.Single().Items.Select(x => x.ProductName).Should().Equal("Segundo");
    }

    [Fact]
    public async Task StoreSales_ShouldReturnFilterTotalsIndependentOfPage()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        for (var index = 0; index < 45; index++)
        {
            await _data.AddOrderAsync(storeId, OrderStatus.Delivered,
                AnalyticsTestData.AtUtc(2026, 10, 29, 10, second: index),
                subtotal: 10m, deliveryFee: 2m);
        }

        var pageOne = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=1&pageSize=20");
        var pageTwo = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=2&pageSize=20");

        pageOne!.TotalItems.Should().Be(45);
        pageOne.TotalPages.Should().Be(3);
        pageTwo!.TotalItems.Should().Be(45);
        pageTwo.Totals.SalesTotal.Should().Be(pageOne.Totals.SalesTotal);
        pageTwo.Totals.DeliveryFees.Should().Be(pageOne.Totals.DeliveryFees);
        pageOne.Totals.SalesTotal.Should().Be(540m);
        pageOne.Totals.DeliveryFees.Should().Be(90m);
    }

    [Fact]
    public async Task StoreSales_ShouldBeConsistentWithAnalyticsForTheSameFilter()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        for (var index = 0; index < 7; index++)
        {
            await _data.AddOrderAsync(storeId, OrderStatus.Delivered,
                AnalyticsTestData.AtUtc(2026, 10, 29, 10, minute: index),
                subtotal: 10m, deliveryFee: 2m);
        }

        var analytics = await client.GetFromJsonAsync<StoreAnalyticsResponseDto>(
            "/api/orders/store/analytics?period=day&anchor=2026-10-29");
        var sales = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=1&pageSize=20");

        sales!.TotalItems.Should().Be(analytics!.Kpis.DeliveredOrders);
        sales.Totals.SalesTotal.Should().Be(analytics.Kpis.Revenue);
        sales.Totals.DeliveryFees.Should().Be(analytics.Kpis.DeliveryFees);
    }

    [Fact]
    public async Task StoreSales_ShouldIsolateSellers()
    {
        var clientA = _factory.CreateClient();
        var (_, storeA, _) = await _data.CreateSellerWithStoreAsync(clientA);
        await _data.AddOrderAsync(storeA, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15),
            subtotal: 10m, items: ("Meu item", 1, 10m));

        var clientB = _factory.CreateClient();
        var (_, storeB, _) = await _data.CreateSellerWithStoreAsync(clientB);
        await _data.AddOrderAsync(storeB, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 16),
            subtotal: 99m, items: ("Item alheio", 1, 99m));

        var payloadA = await clientA.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29");

        payloadA!.TotalItems.Should().Be(1);
        payloadA.Totals.SalesTotal.Should().Be(10m);
        payloadA.Sales.SelectMany(x => x.Items).Should().NotContain(x => x.ProductName == "Item alheio");
    }

    [Fact]
    public async Task StoreSales_ShouldValidatePeriod_AndNormalizePagination()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        await _data.AddOrderAsync(storeId, OrderStatus.Delivered, AnalyticsTestData.AtUtc(2026, 10, 29, 15));

        var invalid = await client.GetAsync("/api/orders/store/sales?period=quarter");
        var normalizedZero = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=0&pageSize=0");
        var oversized = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=1&pageSize=101");

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        normalizedZero!.Page.Should().Be(1);
        normalizedZero.PageSize.Should().Be(20);
        oversized!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task StoreSales_ShouldIncludeMoreThan100OrdersInTotalsButPaginate20()
    {
        var client = _factory.CreateClient();
        var (_, storeId, _) = await _data.CreateSellerWithStoreAsync(client);
        for (var index = 0; index < 105; index++)
        {
            await _data.AddOrderAsync(storeId, OrderStatus.Delivered,
                AnalyticsTestData.AtUtc(2026, 10, 29, 10, minute: index / 60, second: index % 60),
                subtotal: 10m, deliveryFee: 1m);
        }

        var pageOne = await client.GetFromJsonAsync<PagedStoreSalesResponseDto>(
            "/api/orders/store/sales?period=day&anchor=2026-10-29&page=1&pageSize=20");

        pageOne!.TotalItems.Should().Be(105);
        pageOne.TotalPages.Should().Be(6);
        pageOne.Sales.Should().HaveCount(20);
        pageOne.Totals.SalesTotal.Should().Be(1155m);
        pageOne.Totals.DeliveryFees.Should().Be(105m);
    }

    private async Task<string> RegisterAndLoginCustomerAsync(HttpClient client)
    {
        var email = $"sales.customer.{Guid.NewGuid():N}@urbeat.local";
        await client.PostAsJsonAsync("/api/auth/register/customer", new RegisterUserRequestDto
        {
            FullName = "Sales Customer",
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
