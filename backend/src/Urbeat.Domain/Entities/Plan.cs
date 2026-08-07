namespace Urbeat.Domain.Entities;

public sealed class Plan : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}