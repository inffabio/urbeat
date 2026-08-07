namespace Urbeat.Infrastructure.Services.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string FromAddress { get; set; } = "no-reply@urbeat.local";

    public string FromName { get; set; } = "Urbeat";

    public SmtpOptions Smtp { get; set; } = new();

    public bool LogOnly { get; set; } = false;
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool UseStartTls { get; set; } = true;
}
