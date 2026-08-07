using System.Security.Cryptography;
using System.Text;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Urbeat.Infrastructure.Services;

public sealed class CustomerOtpService : ICustomerOtpService
{
    private const string CustomerRole = "Customer";
    private const int CodeLength = 4;
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(1);

    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICustomerVerificationMessageSender _messageSender;
    private readonly ILogger<CustomerOtpService> _logger;

    public CustomerOtpService(
        ApplicationDbContext dbContext,
        UserManager<IdentityUser<Guid>> userManager,
        IJwtTokenService jwtTokenService,
        ICustomerVerificationMessageSender messageSender,
        ILogger<CustomerOtpService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _messageSender = messageSender;
        _logger = logger;
    }

    public async Task<StartCustomerVerificationResponseDto> StartAsync(StartCustomerVerificationRequestDto request, CancellationToken cancellationToken = default)
    {
        var store = await _dbContext.Stores.SingleOrDefaultAsync(x => x.Id == request.StoreId, cancellationToken)
            ?? throw new InvalidOperationException("Loja não encontrada.");

        var senderPhone = DigitsOnly(store.PhoneNumber);
        if (string.IsNullOrWhiteSpace(senderPhone))
        {
            throw new InvalidOperationException("Loja sem telefone configurado para envio do código.");
        }

        var phone = DigitsOnly(request.Customer.PhoneNumber);
        var email = request.Customer.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser<Guid>
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                PhoneNumber = phone,
                PhoneNumberConfirmed = false,
                LockoutEnabled = true
            };

            var password = $"Urbeat@{phone}";
            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(x => x.Description)));
            }
        }
        else
        {
            user.PhoneNumber = phone;
            user.PhoneNumberConfirmed = false;
            await _userManager.UpdateAsync(user);
        }

        UpsertFullNameClaim(user.Id, request.Customer.FullName);
        await EnsureCustomerRoleAsync(user.Id, cancellationToken);
        await EnsureStoreCustomerAsync(store.Id, user.Id, cancellationToken);

        var code = GenerateCode();
        var now = DateTime.UtcNow;
        var verification = new CustomerPhoneVerification
        {
            UserId = user.Id,
            StoreId = store.Id,
            PhoneNumber = phone,
            CodeHash = HashCode(code),
            PendingCep = DigitsOnly(request.Address.Cep),
            PendingStreet = request.Address.Street.Trim(),
            PendingNumber = request.Address.Number.Trim(),
            PendingComplement = string.IsNullOrWhiteSpace(request.Address.Complement) ? null : request.Address.Complement.Trim(),
            PendingNeighborhood = request.Address.Neighborhood.Trim(),
            PendingCity = request.Address.City.Trim(),
            PendingState = request.Address.State.Trim().ToUpperInvariant(),
            ExpiresAtUtc = now.Add(OtpLifetime),
            ResendAvailableAtUtc = now.Add(OtpLifetime),
            Attempts = 0,
            MaxAttempts = 5
        };

        _dbContext.CustomerPhoneVerifications.Add(verification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _messageSender.SendOtpAsync(senderPhone, phone, code, cancellationToken);

        return new StartCustomerVerificationResponseDto
        {
            VerificationId = verification.Id,
            ExpiresAtUtc = verification.ExpiresAtUtc,
            ResendAvailableAtUtc = verification.ResendAvailableAtUtc,
            MaskedPhone = MaskPhone(phone)
        };
    }

    public async Task<ConfirmCustomerVerificationResponseDto> CreateCustomerSessionAsync(StartCustomerVerificationRequestDto request, CancellationToken cancellationToken = default)
    {
        var storeExists = await _dbContext.Stores.AnyAsync(x => x.Id == request.StoreId, cancellationToken);
        if (!storeExists)
        {
            throw new InvalidOperationException("Loja não encontrada.");
        }

        var phone = DigitsOnly(request.Customer.PhoneNumber);
        var email = request.Customer.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser<Guid>
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                PhoneNumber = phone,
                PhoneNumberConfirmed = false,
                LockoutEnabled = true
            };

            var password = $"Urbeat@{phone}";
            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(x => x.Description)));
            }
        }
        else
        {
            user.PhoneNumber = phone;
            await _userManager.UpdateAsync(user);
        }

        UpsertFullNameClaim(user.Id, request.Customer.FullName);
        await EnsureCustomerRoleAsync(user.Id, cancellationToken);
        await EnsureStoreCustomerAsync(request.StoreId, user.Id, cancellationToken);

        var address = await SaveCustomerAddressAsync(user.Id, request.Address, cancellationToken);
        var token = _jwtTokenService.GenerateToken(user.Email ?? email, user.Id, [CustomerRole]);
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = token.RefreshToken,
            ExpiresAtUtc = token.RefreshTokenExpiresAtUtc
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmCustomerVerificationResponseDto
        {
            Succeeded = true,
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            RefreshToken = token.RefreshToken,
            RefreshTokenExpiresAtUtc = token.RefreshTokenExpiresAtUtc,
            CustomerAddressId = address.Id
        };
    }

    public async Task<ConfirmCustomerVerificationResponseDto> ConfirmAsync(ConfirmCustomerVerificationRequestDto request, CancellationToken cancellationToken = default)
    {
        var verification = await _dbContext.CustomerPhoneVerifications.SingleOrDefaultAsync(x => x.Id == request.VerificationId, cancellationToken);
        if (verification is null)
        {
            return Failure("NOT_FOUND", "Verificação não encontrada.");
        }

        if (verification.ConfirmedAtUtc is not null || verification.ConsumedAtUtc is not null)
        {
            return Failure("ALREADY_CONFIRMED", "Código já confirmado.");
        }

        if (verification.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Failure("EXPIRED", "Código expirado. Envie novamente.");
        }

        if (verification.Attempts >= verification.MaxAttempts)
        {
            return Failure("TOO_MANY_ATTEMPTS", "Muitas tentativas. Envie um novo código.");
        }

        if (!FixedTimeEquals(verification.CodeHash, HashCode(request.Code)))
        {
            verification.Attempts += 1;
            verification.MarkAsUpdated();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Failure("INVALID_CODE", "Código inválido.");
        }

        var user = await _userManager.FindByIdAsync(verification.UserId.ToString());
        if (user is null)
        {
            return Failure("USER_NOT_FOUND", "Cliente não encontrado.");
        }

        user.PhoneNumberConfirmed = true;
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        verification.ConfirmedAtUtc = DateTime.UtcNow;
        verification.ConsumedAtUtc = DateTime.UtcNow;
        verification.MarkAsUpdated();

        var address = await SavePendingAddressAsync(user.Id, verification, cancellationToken);

        var token = _jwtTokenService.GenerateToken(user.Email ?? string.Empty, user.Id, [CustomerRole]);
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = token.RefreshToken,
            ExpiresAtUtc = token.RefreshTokenExpiresAtUtc
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmCustomerVerificationResponseDto
        {
            Succeeded = true,
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            RefreshToken = token.RefreshToken,
            RefreshTokenExpiresAtUtc = token.RefreshTokenExpiresAtUtc,
            CustomerAddressId = address?.Id
        };
    }

    public async Task<ResendCustomerVerificationResponseDto> ResendAsync(ResendCustomerVerificationRequestDto request, CancellationToken cancellationToken = default)
    {
        var verification = await _dbContext.CustomerPhoneVerifications.SingleOrDefaultAsync(x => x.Id == request.VerificationId, cancellationToken);
        if (verification is null)
        {
            return new ResendCustomerVerificationResponseDto { Succeeded = false, ErrorCode = "NOT_FOUND", Error = "Verificação não encontrada." };
        }

        if (verification.ResendAvailableAtUtc > DateTime.UtcNow)
        {
            return new ResendCustomerVerificationResponseDto { Succeeded = false, ErrorCode = "TOO_EARLY", Error = "Aguarde o cronômetro para reenviar." };
        }

        var store = await _dbContext.Stores.SingleAsync(x => x.Id == verification.StoreId, cancellationToken);
        var code = GenerateCode();
        var now = DateTime.UtcNow;
        verification.CodeHash = HashCode(code);
        verification.ExpiresAtUtc = now.Add(OtpLifetime);
        verification.ResendAvailableAtUtc = now.Add(OtpLifetime);
        verification.Attempts = 0;
        verification.MarkAsUpdated();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _messageSender.SendOtpAsync(DigitsOnly(store.PhoneNumber), verification.PhoneNumber, code, cancellationToken);

        return new ResendCustomerVerificationResponseDto
        {
            Succeeded = true,
            ExpiresAtUtc = verification.ExpiresAtUtc,
            ResendAvailableAtUtc = verification.ResendAvailableAtUtc
        };
    }

    private async Task<CustomerAddress> SavePendingAddressAsync(Guid userId, CustomerPhoneVerification verification, CancellationToken cancellationToken)
    {
        var existingPrimary = await _dbContext.CustomerAddresses
            .Where(x => x.UserId == userId && x.IsPrimary)
            .ToListAsync(cancellationToken);
        foreach (var existing in existingPrimary)
        {
            existing.IsPrimary = false;
            existing.MarkAsUpdated();
        }

        var address = new CustomerAddress
        {
            UserId = userId,
            Cep = verification.PendingCep,
            Street = verification.PendingStreet,
            Number = verification.PendingNumber,
            Complement = verification.PendingComplement,
            Neighborhood = verification.PendingNeighborhood,
            City = verification.PendingCity,
            State = verification.PendingState,
            IsPrimary = true
        };
        _dbContext.CustomerAddresses.Add(address);
        return address;
    }

    private async Task<CustomerAddress> SaveCustomerAddressAsync(Guid userId, CustomerVerificationAddressDto request, CancellationToken cancellationToken)
    {
        var existingPrimary = await _dbContext.CustomerAddresses
            .Where(x => x.UserId == userId && x.IsPrimary)
            .ToListAsync(cancellationToken);
        foreach (var existing in existingPrimary)
        {
            existing.IsPrimary = false;
            existing.MarkAsUpdated();
        }

        var address = new CustomerAddress
        {
            UserId = userId,
            Cep = DigitsOnly(request.Cep),
            Street = request.Street.Trim(),
            Number = request.Number.Trim(),
            Complement = string.IsNullOrWhiteSpace(request.Complement) ? null : request.Complement.Trim(),
            Neighborhood = request.Neighborhood.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim().ToUpperInvariant(),
            IsPrimary = true
        };
        _dbContext.CustomerAddresses.Add(address);
        return address;
    }

    private async Task EnsureCustomerRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.SingleOrDefaultAsync(x => x.NormalizedName == CustomerRole.ToUpperInvariant(), cancellationToken);
        if (role is null)
        {
            role = new IdentityRole<Guid>(CustomerRole)
            {
                Id = Guid.CreateVersion7(),
                NormalizedName = CustomerRole.ToUpperInvariant()
            };
            _dbContext.Roles.Add(role);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var hasRole = await _dbContext.UserRoles.AnyAsync(x => x.UserId == userId && x.RoleId == role.Id, cancellationToken);
        if (!hasRole)
        {
            _dbContext.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = role.Id
            });
        }
    }

    private async Task EnsureStoreCustomerAsync(Guid storeId, Guid customerUserId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.StoreCustomers
            .AnyAsync(x => x.StoreId == storeId && x.CustomerUserId == customerUserId, cancellationToken);

        if (!exists)
        {
            _dbContext.StoreCustomers.Add(new StoreCustomer
            {
                StoreId = storeId,
                CustomerUserId = customerUserId,
                IsActive = true
            });
        }
    }

    private void UpsertFullNameClaim(Guid userId, string fullName)
    {
        var normalizedFullName = fullName.Trim();
        var claim = _dbContext.UserClaims.SingleOrDefault(x => x.UserId == userId && x.ClaimType == "FullName");
        if (claim is null)
        {
            _dbContext.UserClaims.Add(new IdentityUserClaim<Guid>
            {
                UserId = userId,
                ClaimType = "FullName",
                ClaimValue = normalizedFullName
            });
            return;
        }

        claim.ClaimValue = normalizedFullName;
    }

    private static ConfirmCustomerVerificationResponseDto Failure(string code, string error) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Error = error
    };

    private static string GenerateCode()
    {
        var maxExclusive = (int)Math.Pow(10, CodeLength);
        return RandomNumberGenerator.GetInt32(0, maxExclusive).ToString($"D{CodeLength}");
    }

    private static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static string MaskPhone(string phone)
    {
        if (phone.Length < 4) return phone;
        return new string('*', Math.Max(0, phone.Length - 4)) + phone[^4..];
    }
}
