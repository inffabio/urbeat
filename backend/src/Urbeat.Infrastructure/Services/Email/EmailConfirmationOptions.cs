namespace Urbeat.Infrastructure.Services.Email;

public sealed class EmailConfirmationOptions
{
    public const string SectionName = "EmailConfirmation";

    public string FrontendBaseUrl { get; set; } = "http://localhost:4200";

    public string ConfirmPath { get; set; } = "/confirm-email";
}
