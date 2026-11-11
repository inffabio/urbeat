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
    /// Wire-compatibility only. Older clients may still send it, but the server deliberately
    /// ignores it: ownership of an existing identity is proven by the bearer token of that
    /// identity or by a confirmed single-use e-mail promotion challenge, never by a password
    /// supplied alongside the new role. It is never stored.
    /// </summary>
    public string? CurrentPassword { get; init; }
}