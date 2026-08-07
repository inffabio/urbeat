namespace Urbeat.Application.DTOs;

public sealed class NeighborhoodFreightInfoDto
{
    public Guid Id { get; init; }

    public decimal Rate { get; init; }

    public decimal? MinimumOrderValue { get; init; }

    public int? EstimatedDeliveryTimeMinutes { get; init; }

    public bool Active { get; init; }
}
