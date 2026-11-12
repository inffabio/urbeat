using System.Text.Json.Serialization;

namespace Urbeat.Domain.Enums;

/// <summary>
/// Classificação especial única e opcional de um produto. O valor persistido é o nome
/// estável do membro (<c>Offers</c>, <c>BestSeller</c>, <c>New</c>) e nunca deve ser
/// renumerado. A categoria principal do produto continua sendo <see cref="Entities.Product.CategoryId"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductSpecialClassification
{
    Offers = 1,
    BestSeller = 2,
    New = 3
}
