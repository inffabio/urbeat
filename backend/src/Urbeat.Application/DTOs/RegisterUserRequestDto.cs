using Urbeat.Domain.Entities;

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
    /// Plano escolhido antes do cadastro do lojista. Obrigatório para novos
    /// acessos de vendedor; o servidor valida o catálogo atual e ignora
    /// qualquer valor comercial enviado pelo cliente.
    /// </summary>
    public Guid? PlanId { get; init; }

    /// <summary>
    /// Periodicidade escolhida (mensal ou anual). Obrigatória junto com
    /// <see cref="PlanId"/>; deve estar habilitada no plano.
    /// </summary>
    public SubscriptionBillingPeriod? BillingPeriod { get; init; }

    /// <summary>
    /// Wire-compatibility only. Older clients may still send it, but the server deliberately
    /// ignores it: ownership of an existing identity is proven by the bearer token of that
    /// identity or by a confirmed single-use e-mail promotion challenge, never by a password
    /// supplied alongside the new role. It is never stored.
    /// </summary>
    public string? CurrentPassword { get; init; }
}