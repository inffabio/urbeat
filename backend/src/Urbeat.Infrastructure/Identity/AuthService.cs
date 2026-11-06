using System.Security.Claims;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Domain.Security;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private const string AdminRole = "Admin";

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IEmailChangeChallengeService _emailChangeChallengeService;
    private readonly IdentityOptions _identityOptions;

    public AuthService(
        UserManager<IdentityUser<Guid>> userManager,
        IJwtTokenService jwtTokenService,
        IRefreshTokenRepository refreshTokenRepository,
        RoleManager<IdentityRole<Guid>> roleManager,
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IBackgroundJobClient backgroundJobClient,
        IEmailChangeChallengeService emailChangeChallengeService,
        IOptions<IdentityOptions> identityOptions)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _refreshTokenRepository = refreshTokenRepository;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _backgroundJobClient = backgroundJobClient;
        _emailChangeChallengeService = emailChangeChallengeService;
        _identityOptions = identityOptions.Value;
    }

    public Task<RegistrationResultDto> RegisterCustomerAsync(
        RegisterUserRequestDto request,
        Guid? authenticatedUserId = null,
        IReadOnlyCollection<string>? authenticatedRoles = null,
        CancellationToken cancellationToken = default)
    {
        return RegisterAsync(
            request,
            AccountRoles.Customer,
            UserRoleType.Customer,
            "CustomerRegistered",
            authenticatedUserId,
            authenticatedRoles,
            cancellationToken);
    }

    public Task<RegistrationResultDto> RegisterSellerAsync(
        RegisterUserRequestDto request,
        Guid? authenticatedUserId = null,
        IReadOnlyCollection<string>? authenticatedRoles = null,
        CancellationToken cancellationToken = default)
    {
        return RegisterAsync(
            request,
            AccountRoles.Seller,
            UserRoleType.Seller,
            "SellerRegistered",
            authenticatedUserId,
            authenticatedRoles,
            cancellationToken);
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

        // Admin keeps the legacy single-password flow and never touches UserRoleCredentials.
        if (string.Equals(requiredRole, AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            return await LoginLegacyAsync(user, request, normalizedEmail, ipAddress, cancellationToken);
        }

        if (!AccountRoles.TryParseCredentialRole(requiredRole, out var credentialRole))
        {
            return new LoginResultDto
            {
                Succeeded = false,
                IsForbidden = true,
                Error = "Acesso não autorizado para este tipo de conta."
            };
        }

        return await LoginWithRoleCredentialAsync(user, request, credentialRole, normalizedEmail, ipAddress, cancellationToken);
    }

    private async Task<LoginResultDto> LoginWithRoleCredentialAsync(
        IdentityUser<Guid> user,
        LoginRequestDto request,
        UserRoleType credentialRole,
        string normalizedEmail,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var roleName = AccountRoles.ToRoleName(credentialRole);
        var credential = await FindTrackedCredentialAsync(user.Id, credentialRole, cancellationToken);

        // A user may own the other role only: never validate that other role's password here and
        // never reveal its existence. Missing credential behaves like bad credentials.
        if (credential is null)
        {
            Log.Warning("{EventType} | Login failed | UserId={UserId} | Email={Email} | Role={Role} | IP={IpAddress}",
                "USER_LOGIN_FAILED", user.Id, normalizedEmail, roleName, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginFailed",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login failed for {normalizedEmail}: no credential for the requested role.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                Error = "E-mail ou senha incorretos."
            };
        }

        var utcNow = DateTime.UtcNow;
        if (credential.LockoutEndUtc is { } lockoutEnd && lockoutEnd > utcNow)
        {
            Log.Warning("{EventType} | Login locked out | UserId={UserId} | Email={Email} | Role={Role} | IP={IpAddress}",
                "USER_LOGIN_LOCKED", user.Id, normalizedEmail, roleName, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginLockedOut",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login blocked for {normalizedEmail} as {roleName}: role credential is locked out.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                IsLockedOut = true,
                Error = "Conta bloqueada. Tente novamente mais tarde."
            };
        }

        var verification = _userManager.PasswordHasher.VerifyHashedPassword(user, credential.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            credential.AccessFailedCount++;
            var maxFailedAttempts = Math.Max(1, _identityOptions.Lockout.MaxFailedAccessAttempts);
            var isNowLockedOut = credential.AccessFailedCount >= maxFailedAttempts;
            if (isNowLockedOut)
            {
                credential.LockoutEndUtc = utcNow.Add(_identityOptions.Lockout.DefaultLockoutTimeSpan);
                credential.AccessFailedCount = 0;
            }

            credential.MarkAsUpdated();
            await _dbContext.SaveChangesAsync(cancellationToken);

            Log.Warning("{EventType} | Login failed | UserId={UserId} | Email={Email} | Role={Role} | LockedOut={LockedOut} | IP={IpAddress}",
                isNowLockedOut ? "USER_LOGIN_LOCKED" : "USER_LOGIN_FAILED", user.Id, normalizedEmail, roleName, isNowLockedOut, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: isNowLockedOut ? "LoginLockedOut" : "LoginFailed",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: isNowLockedOut
                    ? $"Login blocked for {normalizedEmail} as {roleName}: lockout threshold reached."
                    : $"Login failed for {normalizedEmail} as {roleName}: invalid password.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                IsLockedOut = isNowLockedOut,
                Error = "Senha incorreta."
            };
        }

        // Defensive: a credential without the Identity role would issue a token with a context
        // the rest of the API does not recognize. Treat it as invalid credentials.
        if (!await _userManager.IsInRoleAsync(user, roleName))
        {
            Log.Warning("{EventType} | Login forbidden | UserId={UserId} | Email={Email} | Role={Role} | IP={IpAddress}",
                "USER_LOGIN_FORBIDDEN", user.Id, normalizedEmail, roleName, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginForbidden",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login denied for {normalizedEmail}: missing role {roleName}.",
                ipAddress: ipAddress,
                cancellationToken);

            return new LoginResultDto
            {
                Succeeded = false,
                Error = "E-mail ou senha incorretos."
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

        credential.AccessFailedCount = 0;
        credential.LockoutEndUtc = null;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = HashPassword(user, request.Password);
        }

        credential.MarkAsUpdated();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Only the requested context goes into the token, even when the user owns both roles.
        var tokenResponse = await IssueTokensAsync(
            user,
            normalizedEmail,
            [roleName],
            roleName,
            cancellationToken);

        Log.Information("{EventType} | Login succeeded | UserId={UserId} | Email={Email} | Role={Role} | IP={IpAddress}",
            "USER_LOGGED_IN", user.Id, normalizedEmail, roleName, ipAddress);
        await WriteAuditLogAsync(
            userId: user.Id,
            auditEvent: "LoginSucceeded",
            entity: nameof(IdentityUser<Guid>),
            entityId: user.Id,
            description: $"Login succeeded for {normalizedEmail} as {roleName}.",
            ipAddress: ipAddress,
            cancellationToken);

        return BuildSuccessfulLoginResult(tokenResponse);
    }

    /// <summary>
    /// Admin (and any future non-credential role) authenticates against the legacy
    /// AspNetUsers.PasswordHash exactly as before. No UserRoleCredentials row is read.
    /// </summary>
    private async Task<LoginResultDto> LoginLegacyAsync(
        IdentityUser<Guid> user,
        LoginRequestDto request,
        string normalizedEmail,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
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
        if (!roles.Contains(AdminRole, StringComparer.OrdinalIgnoreCase))
        {
            Log.Warning("{EventType} | Login forbidden | UserId={UserId} | Email={Email} | IP={IpAddress}", "USER_LOGIN_FORBIDDEN", user.Id, normalizedEmail, ipAddress);
            await WriteAuditLogAsync(
                userId: user.Id,
                auditEvent: "LoginForbidden",
                entity: nameof(IdentityUser<Guid>),
                entityId: user.Id,
                description: $"Login denied for {normalizedEmail}: missing admin role.",
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

        var tokenResponse = await IssueTokensAsync(user, normalizedEmail, roles.ToArray(), refreshRole: null, cancellationToken);

        Log.Information("{EventType} | Login succeeded | UserId={UserId} | Email={Email} | Role={Role} | IP={IpAddress}",
            "USER_LOGGED_IN", user.Id, normalizedEmail, AdminRole, ipAddress);
        await WriteAuditLogAsync(
            userId: user.Id,
            auditEvent: "LoginSucceeded",
            entity: nameof(IdentityUser<Guid>),
            entityId: user.Id,
            description: $"Login succeeded for {normalizedEmail} as {AdminRole}.",
            ipAddress: ipAddress,
            cancellationToken);

        return BuildSuccessfulLoginResult(tokenResponse);
    }

    public async Task<AuthTokenPairDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = RefreshTokenHasher.Hash(refreshToken);

        // Atomic, one-time claim: the token is revoked before the new pair is issued. A replayed
        // or concurrent request with the same token observes a null claim and is rejected.
        var claim = await _refreshTokenRepository.TryClaimAsync(tokenHash, DateTime.UtcNow, cancellationToken);
        if (claim is null || !Guid.TryParse(claim.UserId, out var userId))
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(claim.UserId);
        if (user is null)
        {
            return null;
        }

        string[] roles;
        string? refreshRole = claim.Role;
        if (claim.Role is null)
        {
            // Legacy token issued before role-aware sessions. Keep it usable until the chain is
            // replaced, but converge to a single context when the account owns only one of the
            // credential roles. Accounts with both roles stay ambiguous and keep the old behavior.
            var allRoles = (await _userManager.GetRolesAsync(user)).ToArray();
            var credentialRoles = allRoles
                .Where(roleName => AccountRoles.TryParseCredentialRole(roleName, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (credentialRoles.Length == 1)
            {
                refreshRole = credentialRoles[0];
                roles = credentialRoles;
            }
            else
            {
                roles = allRoles;
            }
        }
        else
        {
            if (!await _userManager.IsInRoleAsync(user, claim.Role))
            {
                return null;
            }

            roles = [claim.Role];
        }

        var tokenResponse = _jwtTokenService.GenerateToken(user.Email ?? string.Empty, user.Id, roles);

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            Role = refreshRole,
            TokenHash = RefreshTokenHasher.Hash(tokenResponse.RefreshToken),
            ExpiresAtUtc = tokenResponse.RefreshTokenExpiresAtUtc
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return tokenResponse;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = RefreshTokenHasher.Hash(refreshToken);
        await _refreshTokenRepository.TryClaimAsync(tokenHash, DateTime.UtcNow, cancellationToken);

        Log.Information("{EventType} | Logout processed | TokenRevoked", "USER_LOGGED_OUT");
    }

    private async Task<AuthTokenPairDto> IssueTokensAsync(
        IdentityUser<Guid> user,
        string email,
        string[] roles,
        string? refreshRole,
        CancellationToken cancellationToken)
    {
        var tokenResponse = _jwtTokenService.GenerateToken(email, user.Id, roles);

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            Role = refreshRole,
            TokenHash = RefreshTokenHasher.Hash(tokenResponse.RefreshToken),
            ExpiresAtUtc = tokenResponse.RefreshTokenExpiresAtUtc
        }, cancellationToken);

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        return tokenResponse;
    }

    private static LoginResultDto BuildSuccessfulLoginResult(AuthTokenPairDto tokenResponse)
    {
        return new LoginResultDto
        {
            Succeeded = true,
            Token = new AuthTokenResponseDto
            {
                AccessToken = tokenResponse.AccessToken,
                ExpiresAtUtc = tokenResponse.ExpiresAtUtc
            },
            RefreshToken = tokenResponse.RefreshToken,
            RefreshTokenExpiresAtUtc = tokenResponse.RefreshTokenExpiresAtUtc
        };
    }

    private async Task<RegistrationResultDto> RegisterAsync(
        RegisterUserRequestDto request,
        string role,
        UserRoleType credentialRole,
        string auditEvent,
        Guid? authenticatedUserId,
        IReadOnlyCollection<string>? authenticatedRoles,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var contractorName = credentialRole == UserRoleType.Seller && !string.IsNullOrWhiteSpace(request.FullName)
            ? request.FullName.Trim()
            : null;

        var outcome = await RunRegistrationCoreAsync(
            request,
            role,
            credentialRole,
            auditEvent,
            normalizedEmail,
            contractorName,
            authenticatedUserId,
            authenticatedRoles,
            cancellationToken);

        if (!outcome.Result.Succeeded)
        {
            _dbContext.ChangeTracker.Clear();
        }

        if (outcome.AuditEvent is not null)
        {
            await WriteAuditLogAsync(
                userId: outcome.AuditUserId,
                auditEvent: outcome.AuditEvent,
                entity: nameof(IdentityUser<Guid>),
                entityId: outcome.AuditUserId,
                description: outcome.AuditDescription!,
                ipAddress: null,
                cancellationToken);
        }

        if (outcome.EnqueueConfirmationUserId is { } enqueueUserId)
        {
            _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(enqueueUserId));
        }

        return outcome.Result;
    }

    private async Task<RegistrationOutcome> RunRegistrationCoreAsync(
        RegisterUserRequestDto request,
        string role,
        UserRoleType credentialRole,
        string auditEvent,
        string normalizedEmail,
        string? contractorName,
        Guid? authenticatedUserId,
        IReadOnlyCollection<string>? authenticatedRoles,
        CancellationToken cancellationToken)
    {
        // The seller promotion (FullName claim + role + credential) must be atomic so a failure
        // never leaves a Seller role without a credential. EF InMemory (integration tests) does
        // not support transactions and executes the same operations sequentially without one.
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Re-registering an e-mail that already owns the role must surface the "account exists"
        // error. The existing-user check therefore runs before any profile update.
        var userAlreadyExists = await _userManager.FindByEmailAsync(normalizedEmail);
        if (userAlreadyExists is not null)
        {
            var isInRole = await _userManager.IsInRoleAsync(userAlreadyExists, role);
            if (isInRole)
            {
                Log.Warning("{EventType} | Registration failed | Email={Email}", "USER_REGISTER_FAILED", normalizedEmail);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = ["An account with this e-mail already exists."]
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Registration failed for {normalizedEmail}: user already has the {role} role.",
                    EnqueueConfirmationUserId: null);
            }

            // Promotion path. Ownership of the existing identity must be proven before a new role
            // and password are attached: either the caller is authenticated as that very user or
            // the supplied CurrentPassword matches an existing credential. The new role password
            // itself is never used as proof of the old account. The failure is generic so it does
            // not reveal the other role's existence.
            var hasOwnershipProof = await HasExistingAccountProofAsync(
                userAlreadyExists,
                request.CurrentPassword,
                authenticatedUserId,
                authenticatedRoles,
                cancellationToken);

            if (!hasOwnershipProof)
            {
                Log.Warning("{EventType} | Registration failed | Email={Email}", "USER_REGISTER_FAILED", normalizedEmail);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = ["An account with this e-mail already exists."]
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Registration failed for {normalizedEmail}: existing account ownership was not proven.",
                    EnqueueConfirmationUserId: null);
            }

            var credentialAlreadyExists = await _dbContext.UserRoleCredentials
                .AnyAsync(x => x.UserId == userAlreadyExists.Id && x.Role == credentialRole, cancellationToken);
            if (credentialAlreadyExists)
            {
                // Inconsistent state (role missing but credential present): never overwrite.
                Log.Warning("{EventType} | Registration failed (credential already exists) | Email={Email} | Role={Role}",
                    "USER_REGISTER_FAILED", normalizedEmail, role);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = ["An account with this e-mail already exists."]
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Registration failed for {normalizedEmail}: a credential for {role} already exists.",
                    EnqueueConfirmationUserId: null);
            }

            var passwordValidation = await ValidatePasswordAsync(userAlreadyExists, request.Password);
            if (!passwordValidation.Succeeded)
            {
                var errors = passwordValidation.Errors.Select(x => x.Description).ToArray();
                Log.Warning("{EventType} | Registration failed (password) | Email={Email} | Errors={Errors}",
                    "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", errors));

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = errors
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Password validation failed for {normalizedEmail}: {string.Join("; ", errors)}",
                    EnqueueConfirmationUserId: null);
            }

            // The FullName claim is persisted before the role so a failed claim never leaves a
            // Seller role without FullName on a non-transactional provider (InMemory). Every
            // IdentityResult is validated. Contractor names are not unique: any number of sellers
            // may share a name.
            if (contractorName is not null)
            {
                var upsertNameResult = await UpsertFullNameClaimAsync(userAlreadyExists, contractorName);
                if (!upsertNameResult.Succeeded)
                {
                    var errors = upsertNameResult.Errors.Select(x => x.Description).ToArray();
                    Log.Warning("{EventType} | Registration failed (claim) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", errors));

                    return new RegistrationOutcome(
                        Result: new RegistrationResultDto
                        {
                            Succeeded = false,
                            Errors = errors
                        },
                        AuditUserId: userAlreadyExists.Id,
                        AuditEvent: $"{auditEvent}Failed",
                        AuditDescription: $"FullName claim update failed for {normalizedEmail}: {string.Join("; ", errors)}",
                        EnqueueConfirmationUserId: null);
                }
            }

            var promotionCredential = await CreateCredentialAsync(userAlreadyExists, credentialRole, request.Password, cancellationToken);

            var addRoleResult = await _userManager.AddToRoleAsync(userAlreadyExists, role);
            if (!addRoleResult.Succeeded)
            {
                _dbContext.UserRoleCredentials.Remove(promotionCredential);
                await _dbContext.SaveChangesAsync(cancellationToken);

                var errors = addRoleResult.Errors.Select(x => x.Description).ToArray();
                Log.Warning("{EventType} | Registration failed (role) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", errors));

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = errors
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Role assignment failed for {normalizedEmail}: {string.Join("; ", errors)}",
                    EnqueueConfirmationUserId: null);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            Log.Information("{EventType} | Added {Role} role and independent credential to existing user | Email={Email}",
                auditEvent, role, normalizedEmail);

            var confirmationPending = !userAlreadyExists.EmailConfirmed;

            return new RegistrationOutcome(
                Result: new RegistrationResultDto
                {
                    Succeeded = true,
                    UserId = userAlreadyExists.Id,
                    EmailConfirmationPending = confirmationPending,
                    RoleAddedToExistingAccount = true,
                    // A confirmed account has no pending e-mail to change; issuing a challenge there
                    // would hand out an unnecessary credential.
                    EmailChangeChallenge = confirmationPending
                        ? await BuildEmailChangeChallengeAsync(userAlreadyExists, normalizedEmail)
                        : null
                },
                AuditUserId: userAlreadyExists.Id,
                AuditEvent: $"{auditEvent}RoleAdded",
                AuditDescription: $"{role} access added to existing identity {normalizedEmail}; the other role credential was preserved.",
                EnqueueConfirmationUserId: confirmationPending ? userAlreadyExists.Id : null);
        }

        if (!string.IsNullOrWhiteSpace(request.Document))
        {
            var cleanDoc = new string(request.Document.Where(char.IsDigit).ToArray());

            if (credentialRole != UserRoleType.Seller)
            {
                var usersWithDoc = await _userManager.GetUsersForClaimAsync(new Claim("Document", cleanDoc));
                var existingUser = usersWithDoc.FirstOrDefault();
                if (existingUser is not null)
                {
                    if (existingUser.EmailConfirmed)
                    {
                        return new RegistrationOutcome(
                            Result: new RegistrationResultDto
                            {
                                Succeeded = false,
                                DocumentAlreadyRegistered = true,
                                Errors = ["CPF já cadastrado."]
                            },
                            AuditUserId: null,
                            AuditEvent: null,
                            AuditDescription: null,
                            EnqueueConfirmationUserId: null);
                    }

                    // The pending account may only be resumed by someone who can prove ownership of
                    // it, so the e-mail change challenge is issued only when the supplied password
                    // validates an existing credential. Otherwise the flow still resends the
                    // confirmation e-mail to the real owner but never exposes a challenge.
                    var pendingOwnerPasswordValid = await VerifyAnyExistingCredentialAsync(
                        existingUser,
                        request.Password,
                        cancellationToken);

                    return new RegistrationOutcome(
                        Result: new RegistrationResultDto
                        {
                            Succeeded = false,
                            DocumentAlreadyRegistered = true,
                            ExistingUserEmail = existingUser.Email,
                            EmailConfirmationPending = true,
                            EmailChangeChallenge = pendingOwnerPasswordValid
                                ? await BuildEmailChangeChallengeAsync(existingUser, existingUser.Email ?? normalizedEmail)
                                : null,
                            Errors = ["CPF já cadastrado. Mas email ainda não confirmado. Um novo link de confirmação foi enviado para o seu e-mail."]
                        },
                        AuditUserId: null,
                        AuditEvent: null,
                        AuditDescription: null,
                        EnqueueConfirmationUserId: existingUser.Id);
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

            return new RegistrationOutcome(
                Result: new RegistrationResultDto
                {
                    Succeeded = false,
                    Errors = errors
                },
                AuditUserId: null,
                AuditEvent: $"{auditEvent}Failed",
                AuditDescription: $"Registration failed for {normalizedEmail}: {string.Join("; ", errors)}",
                EnqueueConfirmationUserId: null);
        }

        if (!string.IsNullOrWhiteSpace(request.Document))
        {
            var cleanDoc = new string(request.Document.Where(char.IsDigit).ToArray());
            var documentClaimResult = await _userManager.AddClaimAsync(user, new Claim("Document", cleanDoc));
            if (!documentClaimResult.Succeeded)
            {
                var errors = documentClaimResult.Errors.Select(x => x.Description).ToArray();
                Log.Warning("{EventType} | Registration failed (document claim) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", errors));
                await TryRemoveUserAsync(user);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = errors
                    },
                    AuditUserId: user.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Document claim persistence failed for {normalizedEmail}: {string.Join("; ", errors)}",
                    EnqueueConfirmationUserId: null);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            var fullNameClaimResult = await _userManager.AddClaimAsync(user, new Claim("FullName", request.FullName.Trim()));
            if (!fullNameClaimResult.Succeeded)
            {
                var errors = fullNameClaimResult.Errors.Select(x => x.Description).ToArray();
                Log.Warning("{EventType} | Registration failed (full name claim) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", errors));
                await TryRemoveUserAsync(user);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        Errors = errors
                    },
                    AuditUserId: user.Id,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"FullName claim persistence failed for {normalizedEmail}: {string.Join("; ", errors)}",
                    EnqueueConfirmationUserId: null);
            }
        }

        var newUserCredential = await CreateCredentialAsync(user, credentialRole, request.Password, cancellationToken);

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            Log.Warning("{EventType} | Registration failed (role) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", roleResult.Errors.Select(x => x.Description)));
            var errors = roleResult.Errors.Select(x => x.Description).ToArray();
            _dbContext.UserRoleCredentials.Remove(newUserCredential);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await TryRemoveUserAsync(user);

            return new RegistrationOutcome(
                Result: new RegistrationResultDto
                {
                    Succeeded = false,
                    Errors = errors
                },
                AuditUserId: user.Id,
                AuditEvent: $"{auditEvent}Failed",
                AuditDescription: $"Role assignment failed for {normalizedEmail}: {string.Join("; ", errors)}",
                EnqueueConfirmationUserId: null);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        Log.Information("{EventType} | Registration succeeded | UserId={UserId} | Email={Email} | Role={Role}", auditEvent, user.Id, normalizedEmail, role);

        return new RegistrationOutcome(
            Result: new RegistrationResultDto
            {
                Succeeded = true,
                UserId = user.Id,
                EmailConfirmationPending = true,
                RoleAddedToExistingAccount = false,
                EmailChangeChallenge = await BuildEmailChangeChallengeAsync(user, normalizedEmail)
            },
            AuditUserId: user.Id,
            AuditEvent: auditEvent,
            AuditDescription: $"User {request.FullName} registered as {role} ({normalizedEmail}).",
            EnqueueConfirmationUserId: user.Id);
    }

    /// <summary>
    /// Ownership proof for adding a role to an existing identity: the caller is authenticated as
    /// that user, or knows the password of a credential the account already owns. During the
    /// migration window (no credential rows yet) the legacy password also proves ownership.
    /// </summary>
    private async Task<bool> HasExistingAccountProofAsync(
        IdentityUser<Guid> user,
        string? currentPassword,
        Guid? authenticatedUserId,
        IReadOnlyCollection<string>? authenticatedRoles,
        CancellationToken cancellationToken)
    {
        if (authenticatedUserId == user.Id && authenticatedRoles is { Count: > 0 })
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            return false;
        }

        return await VerifyAnyExistingCredentialAsync(user, currentPassword, cancellationToken);
    }

    private async Task<bool> VerifyAnyExistingCredentialAsync(
        IdentityUser<Guid> user,
        string password,
        CancellationToken cancellationToken)
    {
        var credentials = await _dbContext.UserRoleCredentials
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.PasswordHash)
            .ToListAsync(cancellationToken);

        if (credentials.Count > 0)
        {
            return credentials.Any(hash =>
                _userManager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed);
        }

        // Migration fallback only: no per-role credential exists yet. Proving knowledge of the
        // legacy password is still proof of ownership, never an authentication source.
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return false;
        }

        return await _userManager.CheckPasswordAsync(user, password);
    }

    private async Task<UserRoleCredential> CreateCredentialAsync(
        IdentityUser<Guid> user,
        UserRoleType role,
        string password,
        CancellationToken cancellationToken)
    {
        var credential = new UserRoleCredential
        {
            UserId = user.Id,
            Role = role,
            PasswordHash = HashPassword(user, password),
            AccessFailedCount = 0,
            LockoutEndUtc = null
        };

        _dbContext.UserRoleCredentials.Add(credential);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return credential;
    }

    private string HashPassword(IdentityUser<Guid> user, string password)
        => _userManager.PasswordHasher.HashPassword(user, password);

    private async Task<IdentityResult> ValidatePasswordAsync(IdentityUser<Guid> user, string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.ToArray());
    }

    private Task<UserRoleCredential?> FindTrackedCredentialAsync(
        Guid userId,
        UserRoleType role,
        CancellationToken cancellationToken)
    {
        return _dbContext.UserRoleCredentials
            .SingleOrDefaultAsync(x => x.UserId == userId && x.Role == role, cancellationToken);
    }

    private async Task<string?> BuildEmailChangeChallengeAsync(IdentityUser<Guid> user, string fallbackEmail)
    {
        var email = string.IsNullOrWhiteSpace(user.Email) ? fallbackEmail : user.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var securityStamp = await _userManager.GetSecurityStampAsync(user);
        if (string.IsNullOrWhiteSpace(securityStamp))
        {
            return null;
        }

        return _emailChangeChallengeService.CreateChallenge(user.Id, email, securityStamp);
    }

    private async Task<IdentityResult> UpsertFullNameClaimAsync(IdentityUser<Guid> user, string fullName)
    {
        var normalizedFullName = fullName.Trim();
        var existingClaim = (await _userManager.GetClaimsAsync(user))
            .FirstOrDefault(c => c.Type == "FullName");

        if (existingClaim is null)
        {
            return await _userManager.AddClaimAsync(user, new Claim("FullName", normalizedFullName));
        }

        if (string.Equals(existingClaim.Value, normalizedFullName, StringComparison.Ordinal))
        {
            return IdentityResult.Success;
        }

        return await _userManager.ReplaceClaimAsync(user, existingClaim, new Claim("FullName", normalizedFullName));
    }

    private async Task TryRemoveUserAsync(IdentityUser<Guid> user)
    {
        try
        {
            await _userManager.DeleteAsync(user);
        }
        catch (Exception ex)
        {
            Log.Warning("{EventType} | Failed to clean up user after failed registration | UserId={UserId} | Error={Error}", "USER_REGISTER_CLEANUP_FAILED", user.Id, ex.Message);
        }
    }

    private sealed record RegistrationOutcome(
        RegistrationResultDto Result,
        Guid? AuditUserId,
        string? AuditEvent,
        string? AuditDescription,
        Guid? EnqueueConfirmationUserId);

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
        if (!AccountRoles.TryParseCredentialRole(request.Role, out var credentialRole))
        {
            return false;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null) return false;

        var credentialExists = await _dbContext.UserRoleCredentials
            .AnyAsync(x => x.UserId == user.Id && x.Role == credentialRole, cancellationToken);
        if (!credentialExists)
        {
            // Do not reveal whether the other role exists: same uniform behavior as unknown e-mail.
            return false;
        }

        // Invalidate previous tokens for this user and role only. A Seller recovery request must
        // never invalidate a Customer recovery token.
        var existingTokens = await _dbContext.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.Role == credentialRole && !t.Used)
            .ToListAsync(cancellationToken);
        foreach (var token in existingTokens)
        {
            token.Used = true;
        }

        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenHash = HashPassword(user, rawToken);
        var roleName = AccountRoles.ToRoleName(credentialRole);

        _dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            Role = credentialRole,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Used = false
        });
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        var userName = user.UserName ?? email;
        var resetLink = $"https://www.urbeat.com.br/redefinir-senha?token={rawToken}&email={Uri.EscapeDataString(email)}&role={roleName}";
        _backgroundJobClient.Enqueue<SendPasswordResetEmailJob>(job => job.ExecuteAsync(email, userName, resetLink));

        // Never log the token or the reset link.
        Log.Information("{EventType} | Password reset requested | Email={Email} | Role={Role}", "PASSWORD_RESET_REQUESTED", email, roleName);
        return true;
    }

    public async Task<ValidateResetTokenResponseDto> ValidateResetTokenAsync(
        string token,
        string? role = null,
        CancellationToken cancellationToken = default)
    {
        UserRoleType? requestedRole = null;
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!AccountRoles.TryParseCredentialRole(role, out var parsedRole))
            {
                return new ValidateResetTokenResponseDto { Valid = false, Message = "Token inválido ou expirado." };
            }

            requestedRole = parsedRole;
        }

        var tokens = await _dbContext.PasswordResetTokens
            .Where(t => !t.Used && t.Role != null && t.ExpiresAt > DateTime.UtcNow)
            .Where(t => requestedRole == null || t.Role == requestedRole)
            .ToListAsync(cancellationToken);

        foreach (var entity in tokens)
        {
            var user = await _userManager.FindByIdAsync(entity.UserId.ToString());
            if (user is null) continue;
            var result = _userManager.PasswordHasher.VerifyHashedPassword(user, entity.TokenHash, token);
            if (result != PasswordVerificationResult.Failed)
            {
                return new ValidateResetTokenResponseDto
                {
                    Valid = true,
                    Role = entity.Role is { } matchedRole ? AccountRoles.ToRoleName(matchedRole) : null
                };
            }
        }

        return new ValidateResetTokenResponseDto { Valid = false, Message = "Token inválido ou expirado." };
    }

    public async Task<(bool Succeeded, string? Error)> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        UserRoleType? requestedRole = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!AccountRoles.TryParseCredentialRole(request.Role, out var parsedRole))
            {
                return (false, "Token inválido ou expirado.");
            }

            requestedRole = parsedRole;
        }

        // Legacy tokens without a role are deliberately not accepted: they could silently change
        // a role the requester does not own.
        var tokens = await _dbContext.PasswordResetTokens
            .Where(t => !t.Used && t.Role != null && t.ExpiresAt > DateTime.UtcNow)
            .Where(t => requestedRole == null || t.Role == requestedRole)
            .ToListAsync(cancellationToken);

        PasswordResetToken? matchedToken = null;
        IdentityUser<Guid>? matchedUser = null;
        foreach (var entity in tokens)
        {
            var user = await _userManager.FindByIdAsync(entity.UserId.ToString());
            if (user is null) continue;
            var result = _userManager.PasswordHasher.VerifyHashedPassword(user, entity.TokenHash, request.Token);
            if (result != PasswordVerificationResult.Failed)
            {
                matchedToken = entity;
                matchedUser = user;
                break;
            }
        }

        if (matchedToken is null || matchedUser is null || matchedToken.Role is null)
            return (false, "Token inválido ou expirado.");

        var passwordValidation = await ValidatePasswordAsync(matchedUser, request.NewPassword);
        if (!passwordValidation.Succeeded)
            return (false, passwordValidation.Errors.First().Description);

        var credential = await FindTrackedCredentialAsync(matchedUser.Id, matchedToken.Role.Value, cancellationToken);
        if (credential is null)
            return (false, "Token inválido ou expirado.");

        // Update only the role credential. The legacy AspNetUsers.PasswordHash stays untouched
        // for rollback safety and is no longer the login source for Customer/Seller.
        credential.PasswordHash = HashPassword(matchedUser, request.NewPassword);
        credential.AccessFailedCount = 0;
        credential.LockoutEndUtc = null;
        credential.MarkAsUpdated();

        matchedToken.Used = true;
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        // Only the recovered role's sessions are revoked; the other role keeps its session.
        // Revocation runs after the credential is persisted so a failed save cannot end a
        // session without changing the password.
        var roleName = AccountRoles.ToRoleName(matchedToken.Role.Value);
        await _refreshTokenRepository.RevokeAllForUserAndRoleAsync(matchedUser.Id, roleName, DateTime.UtcNow, cancellationToken);

        Log.Information("{EventType} | Password reset completed | UserId={UserId} | Role={Role}",
            "PASSWORD_RESET_COMPLETED", matchedUser.Id, roleName);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> ChangePasswordAsync(
        ChangePasswordRequestDto request,
        Guid authenticatedUserId,
        IReadOnlyCollection<string> authenticatedRoles,
        CancellationToken cancellationToken = default)
    {
        if (!AccountRoles.TryParseCredentialRole(request.Role, out var credentialRole))
        {
            return (false, "Tipo de acesso inválido. Use Customer ou Seller.");
        }

        var roleName = AccountRoles.ToRoleName(credentialRole);
        if (!authenticatedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
        {
            return (false, "Acesso não autorizado para este tipo de conta.");
        }

        var user = await _userManager.FindByIdAsync(authenticatedUserId.ToString());
        if (user is null)
        {
            return (false, "Usuário não encontrado.");
        }

        var credential = await FindTrackedCredentialAsync(user.Id, credentialRole, cancellationToken);
        if (credential is null)
        {
            return (false, "Senha atual incorreta.");
        }

        var verification = _userManager.PasswordHasher.VerifyHashedPassword(user, credential.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            return (false, "Senha atual incorreta.");
        }

        var passwordValidation = await ValidatePasswordAsync(user, request.NewPassword);
        if (!passwordValidation.Succeeded)
        {
            return (false, passwordValidation.Errors.First().Description);
        }

        credential.PasswordHash = HashPassword(user, request.NewPassword);
        credential.AccessFailedCount = 0;
        credential.LockoutEndUtc = null;
        credential.MarkAsUpdated();
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        await _refreshTokenRepository.RevokeAllForUserAndRoleAsync(user.Id, roleName, DateTime.UtcNow, cancellationToken);

        Log.Information("{EventType} | Password changed | UserId={UserId} | Role={Role}",
            "PASSWORD_CHANGED", user.Id, roleName);
        return (true, null);
    }

    public async Task<(bool Succeeded, string? Error)> UpdateEmailAsync(UpdateEmailRequestDto request, CancellationToken cancellationToken = default)
    {
        // The signed, expiring, single-use challenge is the credential. UserId/CurrentEmail alone
        // must never authorize a change, otherwise anyone who learns them (they are exposed in the
        // onboarding URL) could hijack an unconfirmed account.
        var validation = _emailChangeChallengeService.ValidateChallenge(request.EmailChangeChallenge);
        if (!validation.Valid)
        {
            Log.Warning("{EventType} | Invalid email change challenge", "EMAIL_UPDATE_CHALLENGE_INVALID");
            return (false, "Desafio de alteração de e-mail inválido ou expirado.");
        }

        var user = await _userManager.FindByIdAsync(validation.UserId.ToString());
        if (user is null)
        {
            Log.Warning("{EventType} | User not found for email update | UserId={UserId}", "EMAIL_UPDATE_USER_NOT_FOUND", validation.UserId);
            return (false, "Usuário não encontrado.");
        }

        // The challenge is bound to the security stamp captured at issuance; a rotated stamp (for
        // example after a previous successful change) makes it unusable, enforcing single use.
        var currentStamp = await _userManager.GetSecurityStampAsync(user);
        if (!string.Equals(currentStamp, validation.SecurityStamp, StringComparison.Ordinal))
        {
            Log.Warning("{EventType} | Email change challenge security stamp mismatch | UserId={UserId}", "EMAIL_UPDATE_CHALLENGE_STALE", user.Id);
            return (false, "Desafio de alteração de e-mail inválido ou expirado.");
        }

        var actualEmail = (user.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.Equals(actualEmail, validation.Email, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("{EventType} | Email change challenge bound to another email | UserId={UserId}", "EMAIL_UPDATE_CHALLENGE_EMAIL_MISMATCH", user.Id);
            return (false, "Desafio de alteração de e-mail inválido ou expirado.");
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentEmail)
            && !string.Equals(request.CurrentEmail.Trim(), actualEmail, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("{EventType} | Current email mismatch | UserId={UserId} | Provided={ProvidedEmail} | Actual={ActualEmail}", "EMAIL_UPDATE_MISMATCH", user.Id, request.CurrentEmail, actualEmail);
            return (false, "E-mail atual não confere.");
        }

        var newEmail = request.NewEmail.Trim().ToLowerInvariant();

        if (string.Equals(actualEmail, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            await ConsumeEmailChangeChallengeAsync(user);
            _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(user.Id));
            Log.Information("{EventType} | Confirmation re-sent for same email | UserId={UserId}", "EMAIL_RESENT_SAME", user.Id);
            return (true, null);
        }

        var exists = await _userManager.FindByEmailAsync(newEmail);
        if (exists is not null)
        {
            Log.Warning("{EventType} | Email already in use | UserId={UserId} | AttemptedEmail={NewEmail} | ExistingUserId={ExistingUserId}", "EMAIL_UPDATE_DUPLICATE", user.Id, newEmail, exists.Id);
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

        await ConsumeEmailChangeChallengeAsync(user);

        _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(user.Id));

        Log.Information("{EventType} | Email updated for confirmation | UserId={UserId} | Old={OldEmail} | New={NewEmail}", "EMAIL_UPDATED", user.Id, actualEmail, newEmail);
        return (true, null);
    }

    private async Task ConsumeEmailChangeChallengeAsync(IdentityUser<Guid> user)
    {
        // Rotating the security stamp invalidates the just-used challenge (and any other outstanding
        // challenge) so it cannot be replayed.
        await _userManager.UpdateSecurityStampAsync(user);
    }
}
