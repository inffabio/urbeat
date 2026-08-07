namespace Urbeat.Domain.Entities;

public sealed class BrazilianState
{
    public int Id { get; set; }
    public string Uf { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Region { get; set; } = string.Empty;

    public ICollection<BrazilianCity> Cities { get; set; } = new List<BrazilianCity>();
}
