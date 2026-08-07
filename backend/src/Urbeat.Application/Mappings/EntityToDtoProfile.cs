using AutoMapper;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.Mappings;

public sealed class EntityToDtoProfile : Profile
{
    public EntityToDtoProfile()
    {
        CreateMap<Store, StoreResponseDto>();
        CreateMap<StoreAddress, StoreAddressResponseDto>();
        CreateMap<StoreBusinessHour, StoreBusinessHourItemDto>();
        CreateMap<StoreBusinessHourShift, StoreBusinessHourShiftDto>();
        CreateMap<StoreDeliveryArea, StoreDeliveryAreaDto>();
        CreateMap<CuisineType, CuisineTypeResponseDto>();
        CreateMap<CustomerAddress, CustomerAddressResponseDto>();
        CreateMap<ProductCategory, ProductCategoryResponseDto>();
        CreateMap<Product, ProductResponseDto>()
            .ForMember(d => d.CategoryName, o => o.Ignore());
        CreateMap<ProductWeightConfig, ProductWeightConfigDto>();
        CreateMap<ProductAdditional, ProductAdditionalDto>();
        CreateMap<ProductChoiceOption, ProductChoiceOptionDto>();
        CreateMap<ProductVariation, ProductVariationDto>();
        CreateMap<ProductOptionGroup, ProductOptionGroupDto>();
        CreateMap<ProductOptionItem, ProductOptionItemDto>();

        CreateMap<StorePaymentGatewayConfig, PaymentGatewayConfigResponseDto>()
            .ForMember(d => d.HasAccessToken, o => o.MapFrom(s => !string.IsNullOrWhiteSpace(s.EncryptedAccessToken)))
            .ForMember(d => d.HasNotificationUrl, o => o.MapFrom(s => !string.IsNullOrWhiteSpace(s.EncryptedNotificationUrl)));
    }
}
