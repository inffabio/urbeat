namespace Urbeat.Domain.Entities;

public sealed class SystemParameter : BaseEntity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public SystemParameterType Type { get; set; } = SystemParameterType.String;

    public string? Description { get; set; }

    public string? Group { get; set; }
}
