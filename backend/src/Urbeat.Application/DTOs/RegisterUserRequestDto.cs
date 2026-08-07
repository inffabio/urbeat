namespace Urbeat.Application.DTOs;

public sealed class RegisterUserRequestDto
{
    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string? PhoneNumber { get; init; }
    
    // Pode ser o CPF (Cliente/Vendedor PF) ou CNPJ (Vendedor PJ)
    public string? Document { get; init; }
}