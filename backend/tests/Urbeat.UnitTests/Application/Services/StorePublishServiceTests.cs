using FluentAssertions;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Interfaces.Publish;
using Urbeat.Application.Services.Publish;

namespace Urbeat.UnitTests.Application.Services;

public sealed class StorePublishServiceTests
{
    private readonly Mock<IStoreService> _storeService = new();
    private readonly Mock<IStoreAddressService> _storeAddressService = new();
    private readonly Mock<IStoreBusinessHoursService> _storeBusinessHoursService = new();
    private readonly Mock<IProductService> _productService = new();

    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    private StorePublishService CreateSut() => new(
        _storeService.Object,
        _storeAddressService.Object,
        _storeBusinessHoursService.Object,
        _productService.Object);

    private StoreResponseDto BuildPublishableStore() => new()
    {
        Id = _storeId,
        OwnerUserId = _ownerId,
        Name = "Loja Teste",
        CuisineType = "Pizza",
        PhoneNumber = "11999999999",
        DeliveryAreas = new[] { new StoreDeliveryAreaDto { Neighborhood = "Centro" } }
    };

    private void SetupCompleteWizard()
    {
        _storeService
            .Setup(x => x.GetByOwnerAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPublishableStore());

        _storeAddressService
            .Setup(x => x.GetByStoreAsync(_ownerId, _storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreAddressResponseDto { StoreId = _storeId, Street = "Rua A", Number = "1", City = "Rio de Janeiro" });

        _storeBusinessHoursService
            .Setup(x => x.GetAsync(_ownerId, _storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreBusinessHoursResponseDto
            {
                StoreId = _storeId,
                Items = new[]
                {
                    new StoreBusinessHourItemDto
                    {
                        DayOfWeek = DayOfWeek.Monday,
                        IsOpen = true,
                        Shifts = new[]
                        {
                            new StoreBusinessHourShiftDto { StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0) }
                        }
                    }
                }
            });

        _productService
            .Setup(x => x.ListByStoreAsync(_ownerId, _storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ProductResponseDto { Id = Guid.NewGuid(), Name = "Pizza", IsAvailable = true } });
    }

    [Fact]
    public async Task PublishStoreAsync_ShouldMarkStoreAsPublished_WhenRequirementsAreMet()
    {
        SetupCompleteWizard();
        _storeService
            .Setup(x => x.UpdateStatusAsync(_ownerId, _storeId, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateStoreResultDto { Store = BuildPublishableStore() });
        _storeService
            .Setup(x => x.MarkAsPublishedAsync(_ownerId, _storeId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateStoreResultDto { Store = BuildPublishableStore() });

        var sut = CreateSut();

        var result = await sut.PublishStoreAsync(_storeId, _ownerId, CancellationToken.None);

        result.Should().BeTrue();
        _storeService.Verify(x => x.MarkAsPublishedAsync(_ownerId, _storeId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishStoreAsync_ShouldNotMarkStoreAsPublished_WhenRequirementsAreNotMet()
    {
        _storeService
            .Setup(x => x.GetByOwnerAsync(_ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildPublishableStore());
        _storeAddressService
            .Setup(x => x.GetByStoreAsync(_ownerId, _storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoreAddressResponseDto?)null);
        _storeBusinessHoursService
            .Setup(x => x.GetAsync(_ownerId, _storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoreBusinessHoursResponseDto { StoreId = _storeId, Items = Array.Empty<StoreBusinessHourItemDto>() });
        _productService
            .Setup(x => x.ListByStoreAsync(_ownerId, _storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProductResponseDto>());

        var sut = CreateSut();

        var result = await sut.PublishStoreAsync(_storeId, _ownerId, CancellationToken.None);

        result.Should().BeFalse();
        _storeService.Verify(x => x.MarkAsPublishedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _storeService.Verify(x => x.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
