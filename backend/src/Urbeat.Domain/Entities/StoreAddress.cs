namespace Urbeat.Domain.Entities;

public sealed class StoreAddress : BaseEntity
{
    public Guid StoreId { get; set; }

    public string Street { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string Neighborhood { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string ZipCode { get; set; } = string.Empty;

    public string? Complement { get; set; }

    public string? Reference { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}