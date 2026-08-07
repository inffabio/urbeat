using System.ComponentModel.DataAnnotations;

namespace Urbeat.Application.Dtos;

public sealed class LandingPageContentRequestDto
{
    [Required]
    [MaxLength(80)]
    public string Section { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }
}
