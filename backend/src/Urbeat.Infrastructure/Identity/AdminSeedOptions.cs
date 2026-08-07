namespace Urbeat.Infrastructure.Identity;

public sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";

    public string Email { get; set; } = "admin@urbeat.local";

    public string Password { get; set; } = "Admin12345";
}