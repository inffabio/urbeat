using Serilog;
using Hangfire;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private const string CustomerRole = "Customer";
    private const string SellerRole = "Seller";

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AuthService(
        UserManager<IdentityUser<Guid>> userManager,
        IJwtTokenService jwtTokenService,
        IRefreshTokenRepository refreshTokenRepository,
        RoleManager<IdentityRole<Guid>> roleManager,
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IBackgroundJobClient backgroundJobClient)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _refreshTokenRepository = refreshTokenRepository;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _backgroundJobClient = backgroundJobClient;
    }

    public Task<RegistrationResultDto> RegisterCustomerAsync(RegisterUserRequestDto request, CancellationToken cancellationToken = default)
    {
        return RegisterAsync(request, CustomerRole, "CustomerRegistered", cancellationToken);
    }

    public Task<RegistrationResultDto> RegisterSellerAsync(RegisterUserRequestDto request, CancellationToken cancellationToken = default)
    {
        return RegisterAsync(request, SellerRole, "SellerRegistered", cancellationToken);
    }

    public async Task<LoginResultDto> LoginAsync(
        LoginRequestDto request,
        string requiredRole,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user is null)
        {
            Log.Information("{EventType} | Login failed | Email={Email} | IP={IpAddress}", "USER_LOGIN_FAILED", normalizedEmail, ipAddress);
            await WriteAuditLogAsync(
                userId: null,
                auditEvent: "LoginFailed",
                entity: nameof(IdentityUser<Guid>),
                entityId: null,
                description: $"Login failed for {normalizedEmail}: user not found.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                Error = "Usuário não encontrado. Verifique o e-mail informado."
            };
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            Log.Warning("{EventType} | Login locked out | UserId={UserId} | Email={Email} | IP={IpAddress}", "USER_LOGIN_LOCKED", user.Id, normalizedEmail, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginLockedOut",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login blocked for {normalizedEmail}: user is locked out.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                IsLockedOut = true,
                Error = "Conta bloqueada. Tente novamente mais tarde."
            };
        }

        var validPassword = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!validPassword)
        {
            await _userManager.AccessFailedAsync(user);
            var isNowLockedOut = await _userManager.IsLockedOutAsync(user);

            Log.Warning("{EventType} | Login failed | UserId={UserId} | Email={Email} | LockedOut={LockedOut} | IP={IpAddress}",
                isNowLockedOut ? "USER_LOGIN_LOCKED" : "USER_LOGIN_FAILED", user.Id, normalizedEmail, isNowLockedOut, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: isNowLockedOut ? "LoginLockedOut" : "LoginFailed",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: isNowLockedOut
                    ? $"Login blocked for {normalizedEmail}: lockout threshold reached."
                    : $"Login failed for {normalizedEmail}: invalid password.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                IsLockedOut = isNowLockedOut,
                Error = "Senha incorreta."
            };
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(requiredRole, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning("{EventType} | Login forbidden | UserId={UserId} | Email={Email} | RequiredRole={RequiredRole} | IP={IpAddress}", "USER_LOGIN_FORBIDDEN", user.Id, normalizedEmail, requiredRole, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginForbidden",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login denied for {normalizedEmail}: missing role {requiredRole}.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                IsForbidden = true,
                Error = "Acesso não autorizado para este tipo de conta."
            };
        }

        if (!user.EmailConfirmed)
        {
            Log.Warning("{EventType} | Login email not confirmed | UserId={UserId} | Email={Email} | IP={IpAddress}",
                "USER_LOGIN_EMAIL_NOT_CONFIRMED", user.Id, normalizedEmail, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginEmailNotConfirmed",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login denied for {normalizedEmail}: e-mail not confirmed.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                IsEmailNotConfirmed = true,
                Error = "E-mail not confirmed. Please check your inbox to confirm your account."
            };
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        var tokenResponse = _jwtTokenService.GenerateToken(user.Email ?? normalizedEmail, user.Id, roles.ToArray());

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            Token = tokenResponse.RefreshToken,
            ExpiresAtUtc = tokenResponse.RefreshTokenExpiresAtUtc
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        Log.Information("{EventType} | Login succeeded | UserId={UserId} | Email={Email} | Role={Role} | IP={IpAddress}", "USER_LOGGED_IN", user.Id, normalizedEmail, requiredRole, ipAddress);
        await WriteAuditLogAsync(
            userId: user.Id,
            auditEvent: "LoginSucceeded",
            entity: nameof(IdentityUser<Guid>),
            entityId: user.Id,
            description: $"Login succeeded for {normalizedEmail} as {requiredRole}.",
            ipAddress: ipAddress,
            cancellationToken);

        return new LoginResultDto
        {
            Succeeded = true,
            Token = tokenResponse
        };
    }

    public async Task<AuthTokenResponseDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var existingToken = await _refreshTokenRepository.GetByTokenAsync(refreshToken, cancellationToken);
        if (existingToken is null || existingToken.IsExpired || existingToken.IsRevoked)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(existingToken.UserId.ToString());
        if (user is null)
        {
            return null;
        }

        existingToken.RevokedAtUtc = DateTime.UtcNow;
        existingToken.MarkAsUpdated();

        var roles = await _userManager.GetRolesAsync(user);
        var tokenResponse = _jwtTokenService.GenerateToken(user.Email ?? string.Empty, user.Id, roles.ToArray());

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            Token = tokenResponse.RefreshToken,
            ExpiresAtUtc = tokenResponse.RefreshTokenExpiresAtUtc
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return tokenResponse;
    }

    private async Task<RegistrationResultDto> RegisterAsync(
        RegisterUserRequestDto request,
        string role,
        string auditEvent,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var userAlreadyExists = await _userManager.FindByEmailAsync(normalizedEmail);
        if (userAlreadyExists is not null)
        {
            var isInRole = await _userManager.IsInRoleAsync(userAlreadyExists, role);
            if (isInRole)
            {
                Log.Warning("{EventType} | Registration failed | Email={Email}", "USER_REGISTER_FAILED", normalizedEmail);
                await WriteAuditLogAsync(
                    userId: userAlreadyExists.Id,
                    auditEvent: $"{auditEvent}Failed",
                    entity: nameof(IdentityUser<Guid>),
                    entityId: userAlreadyExists.Id,
                    description: $"Registration failed for {normalizedEmail}: user already exists.",
                    ipAddress: null,
                    cancellationToken);

                return new RegistrationResultDto
                {
                    Succeeded = false,
                    Errors = ["An account with this e-mail already exists."]
                };
            }

            await _userManager.AddToRoleAsync(userAlreadyExists, role);
            Log.Information("{EventType} | Added {Role} role to existing user | Email={Email}", auditEvent, role, normalizedEmail);

            var confirmationPending = !userAlreadyExists.EmailConfirmed;
            if (confirmationPending)
            {
                _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(userAlreadyExists.Id));
            }

            return new RegistrationResultDto
            {
                Succeeded = true,
                UserId = userAlreadyExists.Id,
                EmailConfirmationPending = confirmationPending,
            };
        }

        if (!string.IsNullOrWhiteSpace(request.Document))
        {
            var cleanDoc = new string(request.Document.Where(char.IsDigit).ToArray());

            if (role != SellerRole)
            {
                var usersWithDoc = await _userManager.GetUsersForClaimAsync(new System.Security.Claims.Claim("Document", cleanDoc));
                var existingUser = usersWithDoc.FirstOrDefault();
                if (existingUser is not null)
                {
                    if (existingUser.EmailConfirmed)
                    {
                        return new RegistrationResultDto
                        {
                            Succeeded = false,
                            DocumentAlreadyRegistered = true,
                            Errors = ["CPF já cadastrado."]
                        };
                    }

                    _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(existingUser.Id));

                    return new RegistrationResultDto
                    {
                        Succeeded = false,
                        DocumentAlreadyRegistered = true,
                        ExistingUserEmail = existingUser.Email,
                        EmailConfirmationPending = true,
                        Errors = ["CPF já cadastrado. Mas email ainda não confirmado. Um novo link de confirmação foi enviado para o seu e-mail."]
                    };
                }
            }
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = false,
            PhoneNumber = request.PhoneNumber?.Trim(),
            LockoutEnabled = true
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            Log.Warning("{EventType} | Registration failed | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", createResult.Errors.Select(x => x.Description)));
            var errors = createResult.Errors.Select(x => x.Description).ToArray();
            await WriteAuditLogAsync(
                userId: null,
                auditEvent: $"{auditEvent}Failed",
                entity: nameof(IdentityUser<Guid>),
                entityId: null,
                description: $"Registration failed for {normalizedEmail}: {string.Join("; ", errors)}",
                ipAddress: null,
                cancellationToken);

            return new RegistrationResultDto
            {
                Succeeded = false,
                Errors = errors
            };
        }
        if (!string.IsNullOrWhiteSpace(request.Document))
        {
            var cleanDoc = new string(request.Document.Where(char.IsDigit).ToArray());
            await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("Document", cleanDoc));
        }
        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            Log.Warning("{EventType} | Registration failed (role) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", roleResult.Errors.Select(x => x.Description)));
            var errors = roleResult.Errors.Select(x => x.Description).ToArray();
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: $"{auditEvent}Failed",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Role assignment failed for {normalizedEmail}: {string.Join("; ", errors)}",
                ipAddress: null,
                cancellationToken);

            return new RegistrationResultDto
            {
                Succeeded = false,
                Errors = errors
            };
        }

        await WriteAuditLogAsync(
            userId: user.Id,
            auditEvent: auditEvent,
            entity: nameof(IdentityUser<Guid>),
            entityId: user.Id,
            description: $"User {request.FullName} registered as {role} ({normalizedEmail}).",
            ipAddress: null,
            cancellationToken);

        Log.Information("{EventType} | Registration succeeded | UserId={UserId} | Email={Email} | Role={Role}", auditEvent, user.Id, normalizedEmail, role);

        // Enqueue confirmation email (fire-and-forget via Hangfire)
        var enqueuedUserId = user.Id;
        _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(enqueuedUserId));

        return new RegistrationResultDto
        {
            Succeeded = true,
            UserId = user.Id,
            EmailConfirmationPending = true
        };
    }

    private async Task WriteAuditLogAsync(
        Guid? userId,
        string auditEvent,
        string entity,
        Guid? entityId,
        string description,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null) return false;

        // Invalidate previous tokens for this user
        var existingTokens = _dbContext.PasswordResetTokens.Where(t => t.UserId == user.Id && !t.Used);
        foreach (var t in existingTokens) t.Used = true;
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenHash = _userManager.PasswordHasher.HashPassword(user, token);

        _dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Used = false
        });
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var userName = user.UserName ?? email;
        var resetLink = $"https://www.urbeat.com.br/redefinir-senha?token={token}&email={Uri.EscapeDataString(email)}";
        _backgroundJobClient.Enqueue<SendPasswordResetEmailJob>(job => job.ExecuteAsync(email, userName, resetLink));

        Log.Information("{EventType} | Password reset requested | Email={Email}", "PASSWORD_RESET_REQUESTED", email);
        return true;
    }

    public async Task<ValidateResetTokenResponseDto> ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.PasswordResetTokens
            .Where(t => !t.Used && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var entity in tokens)
        {
            var user = await _userManager.FindByIdAsync(entity.UserId.ToString());
            if (user is null) continue;
            var result = _userManager.PasswordHasher.VerifyHashedPassword(user, entity.TokenHash, token);
            if (result == PasswordVerificationResult.Success)
                return new ValidateResetTokenResponseDto { Valid = true };
        }

        return new ValidateResetTokenResponseDto { Valid = false, Message = "Token inválido ou expirado." };
    }

    public async Task<(bool Succeeded, string? Error)> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.PasswordResetTokens
            .Where(t => !t.Used && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        PasswordResetToken? matchedToken = null;
        IdentityUser<Guid>? matchedUser = null;
        foreach (var entity in tokens)
        {
            var user = await _userManager.FindByIdAsync(entity.UserId.ToString());
            if (user is null) continue;
            var result = _userManager.PasswordHasher.VerifyHashedPassword(user, entity.TokenHash, request.Token);
            if (result == PasswordVerificationResult.Success)
            {
                matchedToken = entity;
                matchedUser = user;
                break;
            }
        }

        if (matchedToken is null || matchedUser is null)
            return (false, "Token inválido ou expirado.");

        var resetResult = await _userManager.ResetPasswordAsync(matchedUser, await _userManager.GeneratePasswordResetTokenAsync(matchedUser), request.NewPassword);
        if (!resetResult.Succeeded)
            return (false, resetResult.Errors.First().Description);

        matchedToken.Used = true;
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        Log.Information("{EventType} | Password reset completed | UserId={UserId}", "PASSWORD_RESET_COMPLETED", matchedUser.Id);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> UpdateEmailAsync(Guid userId, UpdateEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            Log.Warning("{EventType} | User not found for email update | UserId={UserId}", "EMAIL_UPDATE_USER_NOT_FOUND", userId);
            return (false, "Usuário não encontrado.");
        }

        if (!string.Equals(user.Email, request.CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("{EventType} | Current email mismatch | UserId={UserId} | Provided={ProvidedEmail} | Actual={ActualEmail}", "EMAIL_UPDATE_MISMATCH", userId, request.CurrentEmail, user.Email);
            return (false, "E-mail atual não confere.");
        }

        var newEmail = request.NewEmail.Trim().ToLowerInvariant();

        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(user.Id));
            Log.Information("{EventType} | Confirmation re-sent for same email | UserId={UserId}", "EMAIL_RESENT_SAME", user.Id);
            return (true, null);
        }

        var exists = await _userManager.FindByEmailAsync(newEmail);
        if (exists is not null)
        {
            Log.Warning("{EventType} | Email already in use | UserId={UserId} | AttemptedEmail={NewEmail} | ExistingUserId={ExistingUserId}", "EMAIL_UPDATE_DUPLICATE", userId, newEmail, exists.Id);
            return (false, "Este e-mail já está em uso.");
        }

        var setEmailResult = await _userManager.SetEmailAsync(user, newEmail);
        if (!setEmailResult.Succeeded)
        {
            Log.Warning("{EventType} | Email update failed (SetEmail) | UserId={UserId} | NewEmail={NewEmail} | Errors={Errors}", "EMAIL_UPDATE_FAILED", user.Id, newEmail, setEmailResult.Errors);
            return (false, "Não foi possível atualizar o e-mail.");
        }

        var setUserNameResult = await _userManager.SetUserNameAsync(user, newEmail);
        if (!setUserNameResult.Succeeded)
        {
            Log.Warning("{EventType} | Username update failed | UserId={UserId} | NewUserName={NewEmail} | Errors={Errors}", "USERNAME_UPDATE_FAILED", user.Id, newEmail, setUserNameResult.Errors);
            return (false, "Não foi possível atualizar o e-mail.");
        }

        user.EmailConfirmed = false;
        await _userManager.UpdateAsync(user);

        _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(user.Id));

        Log.Information("{EventType} | Email updated for confirmation | UserId={UserId} | Old={OldEmail} | New={NewEmail}", "EMAIL_UPDATED", user.Id, request.CurrentEmail, newEmail);
        return (true, null);
    }
}