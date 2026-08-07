namespace Urbeat.Domain.Entities;

public sealed class LandingPageContent : BaseEntity
{
    public string Section { get; set; } = string.Empty; // e.g., "Hero", "Stats", "Plans", "Testimonials"
    public string Key { get; set; } = string.Empty;     // e.g., "Title", "Subtitle", "Stat1Value"
    public string Value { get; set; } = string.Empty;   // The actual content
    public int DisplayOrder { get; set; }               // For ordering items within a section
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }            // Optional description for admin reference
}
