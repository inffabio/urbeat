using System.Text.Json;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Application.Dtos;

public sealed class StoreAnalyticsDtoSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void StoreAnalyticsResponse_ShouldSerializeContractKeysAndFormats()
    {
        var response = new StoreAnalyticsResponseDto
        {
            Period = "week",
            AnchorDate = new DateOnly(2026, 10, 29),
            WeekBlock = 5,
            RangeStartUtc = new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc),
            RangeEndUtcExclusive = new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            TimeZone = "America/Sao_Paulo",
            ChartYear = 2026,
            Kpis = new StoreAnalyticsKpisDto
            {
                DeliveredOrders = 12,
                Revenue = 1234.50m,
                DeliveryFees = 60m,
                AverageTicket = 102.88m
            },
            Products = [new StoreAnalyticsProductDto { ProductName = "X-Burger", Quantity = 30, Revenue = 900m }],
            Payments = [new StoreAnalyticsPaymentDto { PaymentMethod = PaymentMethod.PixOnline, DeliveredOrders = 7, Revenue = 800m }],
            Months = [new StoreAnalyticsMonthDto { Year = 2026, Month = 1, DeliveredOrders = 0, Revenue = 0m }],
            AvailableYears = [2026, 2025]
        };

        var json = JsonSerializer.Serialize(response, Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("period", out _).Should().BeTrue();
        root.TryGetProperty("anchorDate", out _).Should().BeTrue();
        root.TryGetProperty("weekBlock", out _).Should().BeTrue();
        root.TryGetProperty("rangeStartUtc", out _).Should().BeTrue();
        root.TryGetProperty("rangeEndUtcExclusive", out _).Should().BeTrue();
        root.TryGetProperty("timeZone", out _).Should().BeTrue();
        root.TryGetProperty("chartYear", out _).Should().BeTrue();
        root.TryGetProperty("kpis", out _).Should().BeTrue();
        root.TryGetProperty("products", out _).Should().BeTrue();
        root.TryGetProperty("payments", out _).Should().BeTrue();
        root.TryGetProperty("months", out _).Should().BeTrue();
        root.TryGetProperty("availableYears", out _).Should().BeTrue();

        root.GetProperty("anchorDate").GetString().Should().Be("2026-10-29");
        root.GetProperty("rangeStartUtc").GetString().Should().EndWith("Z");
        root.GetProperty("payments")[0].GetProperty("paymentMethod").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("payments")[0].GetProperty("paymentMethod").GetInt32().Should().Be(1);

        var roundTrip = JsonSerializer.Deserialize<StoreAnalyticsResponseDto>(json, Options);
        roundTrip.Should().NotBeNull();
        roundTrip!.AnchorDate.Should().Be(new DateOnly(2026, 10, 29));
        roundTrip.RangeStartUtc.Should().Be(response.RangeStartUtc);
        roundTrip.Kpis.Revenue.Should().Be(1234.50m);
        roundTrip.Payments.Should().ContainSingle(x => x.PaymentMethod == PaymentMethod.PixOnline);
        roundTrip.Months.Should().ContainSingle();
    }

    [Fact]
    public void PagedStoreSalesResponse_ShouldSerializePiiAsNullAndTotalsAsNumbers()
    {
        var response = new PagedStoreSalesResponseDto
        {
            Period = "day",
            AnchorDate = new DateOnly(2026, 10, 29),
            WeekBlock = null,
            RangeStartUtc = new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc),
            RangeEndUtcExclusive = new DateTime(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc),
            TimeZone = "America/Sao_Paulo",
            Page = 1,
            PageSize = 20,
            TotalItems = 1,
            TotalPages = 1,
            Totals = new StoreSalesTotalsDto { SalesTotal = 12345.67m, DeliveryFees = 456.78m },
            Sales =
            [
                new StoreSaleRowDto
                {
                    OrderId = Guid.Parse("0199f2c4-5a7b-7c3d-8e9f-000000000001"),
                    Code = "A1B2C3D4",
                    CustomerName = null,
                    CreatedAtUtc = new DateTime(2026, 10, 29, 16, 42, 0, DateTimeKind.Utc),
                    Subtotal = 80m,
                    DeliveryFee = 5m,
                    Total = 85m,
                    Items =
                    [
                        new StoreSaleItemDto
                        {
                            ProductName = "X-Burger",
                            VariationName = null,
                            Quantity = 2,
                            UnitPrice = 35m,
                            TotalPrice = 70m
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(response, Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("weekBlock", out var weekBlock).Should().BeTrue();
        weekBlock.ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("totals").GetProperty("salesTotal").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("totals").GetProperty("deliveryFees").ValueKind.Should().Be(JsonValueKind.Number);
        root.GetProperty("sales")[0].GetProperty("customerName").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("sales")[0].GetProperty("items")[0].GetProperty("variationName").ValueKind.Should().Be(JsonValueKind.Null);
        root.TryGetProperty("customerUserId", out _).Should().BeFalse();

        var roundTrip = JsonSerializer.Deserialize<PagedStoreSalesResponseDto>(json, Options);
        roundTrip!.Sales.Should().ContainSingle();
        roundTrip.Sales.First().CustomerName.Should().BeNull();
        roundTrip.Totals.SalesTotal.Should().Be(12345.67m);
    }

    [Fact]
    public void StoreSalesQueryDto_ShouldInheritAnalyticsQuery()
    {
        var query = new StoreSalesQueryDto
        {
            Period = "month",
            Anchor = "2026-10-01",
            WeekBlock = 2,
            Page = 2,
            PageSize = 20
        };

        query.Should().BeAssignableTo<StoreAnalyticsQueryDto>();
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(20);
    }
}
