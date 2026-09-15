using System.Reflection;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.DTOs.Publish;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Domain;

public sealed class StoreDescriptionRemovalTests
{
    private const string RemovedField = "Description";

    [Theory]
    [InlineData(typeof(Store))]
    [InlineData(typeof(CreateStoreRequestDto))]
    [InlineData(typeof(UpdateStoreRequestDto))]
    [InlineData(typeof(StoreResponseDto))]
    [InlineData(typeof(StorePublicDetailsDto))]
    [InlineData(typeof(StoreDetailsSummaryDto))]
    public void StoreContract_ShouldNotDeclareDescription(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().NotContain(RemovedField);
    }

    [Theory]
    [InlineData(typeof(Store))]
    [InlineData(typeof(CreateStoreRequestDto))]
    [InlineData(typeof(UpdateStoreRequestDto))]
    [InlineData(typeof(StoreResponseDto))]
    [InlineData(typeof(StorePublicDetailsDto))]
    [InlineData(typeof(StoreDetailsSummaryDto))]
    public void StoreContract_ShouldKeepNameAndLogo(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().Contain("Name");

        if (type != typeof(StoreDetailsSummaryDto))
        {
            declared.Should().Contain("LogoUrl");
        }
    }
}
