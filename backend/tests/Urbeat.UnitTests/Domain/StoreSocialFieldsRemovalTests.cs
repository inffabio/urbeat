using System.Reflection;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Validators;
using Urbeat.Domain.Entities;

namespace Urbeat.UnitTests.Domain;

public sealed class StoreSocialFieldsRemovalTests
{
    private static readonly string[] RemovedFields = ["InstagramUrl", "FacebookUrl", "TikTokUrl"];

    [Theory]
    [InlineData(typeof(Store))]
    [InlineData(typeof(CreateStoreRequestDto))]
    [InlineData(typeof(UpdateStoreRequestDto))]
    [InlineData(typeof(StoreResponseDto))]
    public void Type_ShouldNotDeclareSocialMediaFields(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().NotContain(RemovedFields);
    }

    [Theory]
    [InlineData(typeof(Store))]
    [InlineData(typeof(CreateStoreRequestDto))]
    [InlineData(typeof(UpdateStoreRequestDto))]
    [InlineData(typeof(StoreResponseDto))]
    public void Type_ShouldKeepWebsiteUrl(Type type)
    {
        var declared = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        declared.Should().Contain("WebsiteUrl");
    }

    [Fact]
    public async Task CreateStoreValidator_ShouldAcceptWebsiteUrlUpToFiveHundredCharacters()
    {
        var request = BuildCreateRequest(websiteUrl: new string('a', 500));

        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateStoreValidator_ShouldRejectWebsiteUrlLongerThanFiveHundredCharacters()
    {
        var request = BuildCreateRequest(websiteUrl: new string('a', 501));

        var result = await new CreateStoreRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(CreateStoreRequestDto.WebsiteUrl));
    }

    [Fact]
    public async Task UpdateStoreValidator_ShouldAcceptWebsiteUrlUpToFiveHundredCharacters()
    {
        var request = BuildUpdateRequest(websiteUrl: new string('a', 500));

        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateStoreValidator_ShouldRejectWebsiteUrlLongerThanFiveHundredCharacters()
    {
        var request = BuildUpdateRequest(websiteUrl: new string('a', 501));

        var result = await new UpdateStoreRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateStoreRequestDto.WebsiteUrl));
    }

    private static CreateStoreRequestDto BuildCreateRequest(string? websiteUrl = null) => new()
    {
        Name = "Loja Teste",
        Slug = "loja-teste",
        PhoneNumber = "21999999999",
        WebsiteUrl = websiteUrl,
        CuisineType = "Lanches",
        SupportsDelivery = true,
        SupportsPickup = true,
        MaxDeliveryRadiusKm = 10,
    };

    private static UpdateStoreRequestDto BuildUpdateRequest(string? websiteUrl = null) => new()
    {
        Name = "Loja Teste",
        Slug = "loja-teste",
        PhoneNumber = "21999999999",
        WebsiteUrl = websiteUrl,
        CuisineType = "Lanches",
        SupportsDelivery = true,
        SupportsPickup = true,
        MaxDeliveryRadiusKm = 10,
    };
}
