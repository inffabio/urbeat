namespace Urbeat.Domain.Entities;

public sealed class CustomerAddress : BaseEntity
{
    public Guid UserId { get; set; }

    public string Cep { get; set; } = string.Empty;

    public string Street { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string Neighborhood { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string? Complement { get; set; }

    public string? Reference { get; set; }

    public bool IsPrimary { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}
