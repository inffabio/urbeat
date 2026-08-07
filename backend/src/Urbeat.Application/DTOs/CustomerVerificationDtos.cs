namespace Urbeat.Application.DTOs;

public sealed class StartCustomerVerificationRequestDto
{
    public Guid StoreId { get; init; }

    public CustomerVerificationCustomerDto Customer { get; init; } = new();

    public CustomerVerificationAddressDto Address { get; init; } = new();
}

public sealed class CustomerVerificationCustomerDto
{
    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;
}

public sealed class CustomerVerificationAddressDto
{
    public string Cep { get; init; } = string.Empty;

    public string Street { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public string? Complement { get; init; }

    public string Neighborhood { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;
}

public sealed class StartCustomerVerificationResponseDto
{
    public Guid VerificationId { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime ResendAvailableAtUtc { get; init; }

    public string MaskedPhone { get; init; } = string.Empty;
}

public sealed class ConfirmCustomerVerificationRequestDto
{
    public Guid VerificationId { get; init; }

    public string Code { get; init; } = string.Empty;
}

public sealed class ConfirmCustomerVerificationResponseDto
{
    public bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public string? Error { get; init; }

    public string? AccessToken { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public string? RefreshToken { get; init; }

    public DateTime? RefreshTokenExpiresAtUtc { get; init; }

    public Guid? CustomerAddressId { get; init; }
}

public sealed class ResendCustomerVerificationRequestDto
{
    public Guid VerificationId { get; init; }
}

public sealed class ResendCustomerVerificationResponseDto
{
    public bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public string? Error { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public DateTime? ResendAvailableAtUtc { get; init; }
}
