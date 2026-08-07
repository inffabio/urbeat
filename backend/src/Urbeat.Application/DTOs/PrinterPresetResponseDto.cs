namespace Urbeat.Application.Dtos;

public sealed class PrinterPresetResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public string PaperWidth { get; set; } = string.Empty;
    public string CommandSet { get; set; } = string.Empty;
    public string AdapterId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
