namespace Urbeat.Application.DTOs;

public sealed class UpdateStoreCustomerRequestDto
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Cep { get; init; }
    public string? Street { get; init; }
    public string? Number { get; init; }
    public string? Complement { get; init; }
    public string? Neighborhood { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
}

public sealed class UpdateStoreCustomerStatusRequestDto
{
    public bool IsActive { get; init; }
}

public sealed class UpdateStoreCustomerResultDto
{
    public bool NotFound { get; init; }
    public bool Forbidden { get; init; }
    public bool Conflict { get; init; }
    public SellerCustomerSummaryResponseDto? Customer { get; init; }
}
