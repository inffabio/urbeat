namespace Urbeat.Domain.Entities;

public sealed class BrazilianCity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int StateId { get; set; }
    public BrazilianState State { get; set; } = null!;
}
