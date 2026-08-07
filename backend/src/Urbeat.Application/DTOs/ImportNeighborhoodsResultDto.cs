namespace Urbeat.Application.DTOs;

public sealed class ImportNeighborhoodsResultDto
{
    public string City { get; init; } = string.Empty;

    public string Uf { get; init; } = string.Empty;

    public int Found { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Ignored { get; init; }
}
