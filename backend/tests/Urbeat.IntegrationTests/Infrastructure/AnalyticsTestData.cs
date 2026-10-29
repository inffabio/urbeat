using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Builds deterministic analytics fixtures. Stores are created through the public API so
/// authorization data is real; orders are seeded directly because the scenarios need explicit
/// CreatedAtUtc values and statuses that the checkout flow would take many requests to reach.
/// </summary>
public sealed class AnalyticsTestData
{
    private const string Password = "SenhaForte123";

    private readonly TestWebApplicationFactory _factory;

    public AnalyticsTestData(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task<(string AccessToken, Guid StoreId, Guid OwnerUserId)> CreateSellerWithStoreAsync(
        HttpClient client,
        string cuisineType = "Pizza")
    {
        var email = $"analytics.seller.{Guid.NewGuid():N}@urbeat.local";

        await _factory.RegisterSellerAsync(client, email, Password, "Analytics Seller", "11983330000");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = Password
        });
        loginResponse.EnsureSuccessStatusCode();
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        token.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Analytics",
            Slug = $"loja-analytics-{Guid.NewGuid():N}",
            PhoneNumber = "11987770000",
            CuisineType = cuisineType,
            MaxDeliveryRadiusKm = 5
        });
        createStoreResponse.EnsureSuccessStatusCode();
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();
        store.Should().NotBeNull();

        return (token.AccessToken, store!.Id, store.OwnerUserId);
    }

    public async Task<string> CreateSellerWithoutStoreAsync(HttpClient client)
    {
        var email = $"analytics.nostore.{Guid.NewGuid():N}@urbeat.local";
        await _factory.RegisterSellerAsync(client, email, Password, "Analytics No Store", "11984440000");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = Password
        });
        loginResponse.EnsureSuccessStatusCode();
        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return token.AccessToken;
    }

    public async Task<Guid> AddOrderAsync(
        Guid storeId,
        OrderStatus status,
        DateTime createdAtUtc,
        decimal subtotal = 10m,
        decimal deliveryFee = 0m,
        PaymentMethod paymentMethod = PaymentMethod.PixOnline,
        Guid? customerUserId = null,
        string? customerName = null,
        string? addressStreet = null,
        string? notes = null,
        params (string ProductName, int Quantity, decimal UnitPrice)[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order
        {
            Code = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            CustomerUserId = customerUserId ?? Guid.NewGuid(),
            StoreId = storeId,
            FulfillmentType = FulfillmentType.Delivery,
            PaymentMethod = paymentMethod,
            Status = status,
            Subtotal = subtotal,
            DeliveryFee = deliveryFee,
            Total = subtotal + deliveryFee,
            AddressStreet = addressStreet,
            Notes = notes
        };

        SetCreatedAtUtc(order, createdAtUtc);
        db.Orders.Add(order);

        foreach (var item in items)
        {
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.Quantity * item.UnitPrice
            });
        }

        if (!string.IsNullOrWhiteSpace(customerName))
        {
            db.UserClaims.Add(new IdentityUserClaim<Guid>
            {
                UserId = order.CustomerUserId,
                ClaimType = "FullName",
                ClaimValue = customerName
            });
        }

        await db.SaveChangesAsync();
        return order.Id;
    }

    public static DateTime AtUtc(int year, int month, int day, int hour, int minute = 0, int second = 0)
        => new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static void SetCreatedAtUtc(BaseEntity entity, DateTime value)
        => typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAtUtc))!
            .SetValue(entity, DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
