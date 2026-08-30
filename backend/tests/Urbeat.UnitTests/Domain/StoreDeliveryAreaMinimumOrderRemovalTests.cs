using System.Reflection;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Domain;

public sealed class StoreDeliveryAreaMinimumOrderRemovalTests
{
    private static readonly string[] RemovedAreaFields = ["MinimumOrderValue", "FreeShippingThreshold"];

    [Theory]
    [InlineData(typeof(StoreDeliveryArea))]
    [InlineData(typeof(StoreDeliveryAreaDto))]
    [InlineData(typeof(NeighborhoodMapItemDto))]
    [InlineData(typeof(NeighborhoodFreightInfoDto))]
    public void AreaContract_ShouldNotDeclareRemovedPerAreaFields(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().NotContain(RemovedAreaFields);
    }

    [Theory]
    [InlineData(typeof(Store))]
    [InlineData(typeof(StoreResponseDto))]
    [InlineData(typeof(StorePublicDetailsDto))]
    [InlineData(typeof(StorePublicListItemDto))]
    [InlineData(typeof(UpdateStoreDeliveryConfigRequestDto))]
    public void GlobalType_ShouldKeepMinimumOrderValue(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().Contain("MinimumOrderValue");
    }

    [Theory]
    [InlineData(typeof(Store))]
    [InlineData(typeof(StoreResponseDto))]
    [InlineData(typeof(StorePublicDetailsDto))]
    [InlineData(typeof(StorePublicListItemDto))]
    [InlineData(typeof(UpdateStoreDeliveryConfigRequestDto))]
    public void GlobalType_ShouldKeepFreeShippingThreshold(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().Contain("FreeShippingThreshold");
    }
}
