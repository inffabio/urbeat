namespace Urbeat.Application.DTOs;

public sealed class ConfirmRolePromotionRequestDto
{
    public string Token { get; init; } = string.Empty;
}

public sealed class ConfirmRolePromotionResultDto
{
    public bool Succeeded { get; init; }

    /// <summary>
    /// True when the same valid challenge had already been applied before. The endpoint is
    /// idempotent: replaying a successfully consumed link never duplicates the role or credential.
    /// </summary>
    public bool AlreadyApplied { get; init; }

    /// <summary>Role that was activated (Customer or Seller).</summary>
    public string? Role { get; init; }

    public bool EmailConfirmationPending { get; init; }

    public string? Error { get; init; }
}
