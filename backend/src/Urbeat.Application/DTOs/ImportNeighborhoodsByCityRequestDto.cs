namespace Urbeat.Application.DTOs;

public sealed class ImportNeighborhoodsByCityRequestDto
{
    public string City { get; init; } = string.Empty;

    public string Uf { get; init; } = string.Empty;

    public Guid? StoreId { get; init; }
}
