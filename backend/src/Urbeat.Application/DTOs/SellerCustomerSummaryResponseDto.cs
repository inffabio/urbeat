namespace Urbeat.Application.DTOs;

public sealed class SellerCustomerSummaryResponseDto
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;
    public string Cep { get; init; } = string.Empty;
    public string Street { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string Complement { get; init; } = string.Empty;
    public string Neighborhood { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;

    public int TotalOrders { get; init; }

    public decimal TotalSpent { get; init; }

    public DateTime? LastOrderAtUtc { get; init; }

    public bool IsActive { get; init; }
}
