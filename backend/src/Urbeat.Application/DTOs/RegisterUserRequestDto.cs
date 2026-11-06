namespace Urbeat.Application.DTOs;

public sealed class RegisterUserRequestDto
{
    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }
    
    // Pode ser o CPF (Cliente/Vendedor PF) ou CNPJ (Vendedor PJ)
    public string? Document { get; init; }

    /// <summary>
    /// Proof that the caller owns an existing account when a new role is being added to an
    /// e-mail that already exists. It is the password of any role the user already owns (or,
    /// during migration only, the legacy password). It is never stored.
    /// </summary>
    public string? CurrentPassword { get; init; }
}