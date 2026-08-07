namespace Urbeat.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "urbeat";

    public string Audience { get; set; } = "urbeat-api";

    public string Secret { get; set; } = "CHANGE_ME_MINIMUM_32_CHARS_SECRET";

    public int ExpirationMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;
}
