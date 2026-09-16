using System.Data;
using Serilog;
using Hangfire;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Domain.Security;
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
    private readonly IEmailChangeChallengeService _emailChangeChallengeService;

    public AuthService(
        UserManager<IdentityUser<Guid>> userManager,
        IJwtTokenService jwtTokenService,
        IRefreshTokenRepository refreshTokenRepository,
        RoleManager<IdentityRole<Guid>> roleManager,
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IBackgroundJobClient backgroundJobClient,
        IEmailChangeChallengeService emailChangeChallengeService)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _refreshTokenRepository = refreshTokenRepository;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _backgroundJobClient = backgroundJobClient;
        _emailChangeChallengeService = emailChangeChallengeService;
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
            TokenHash = RefreshTokenHasher.Hash(tokenResponse.RefreshToken),
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
            Token = new AuthTokenResponseDto
            {
                AccessToken = tokenResponse.AccessToken,
                ExpiresAtUtc = tokenResponse.ExpiresAtUtc
            },
            RefreshToken = tokenResponse.RefreshToken,
            RefreshTokenExpiresAtUtc = tokenResponse.RefreshTokenExpiresAtUtc
        };
    }

    public async Task<AuthTokenPairDto?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = RefreshTokenHasher.Hash(refreshToken);

        // Atomic, one-time claim: the token is revoked before the new pair is issued. A replayed
        // or concurrent request with the same token observes a null claim and is rejected.
        var claimedUserId = await _refreshTokenRepository.TryClaimAsync(tokenHash, DateTime.UtcNow, cancellationToken);
        if (claimedUserId is null || !Guid.TryParse(claimedUserId, out var userId))
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(claimedUserId);
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var tokenResponse = _jwtTokenService.GenerateToken(user.Email ?? string.Empty, user.Id, roles.ToArray());

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
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

    private async Task<RegistrationResultDto> RegisterAsync(
        RegisterUserRequestDto request,
        string role,
        string auditEvent,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var contractorName = role == SellerRole && !string.IsNullOrWhiteSpace(request.FullName)
            ? request.FullName.Trim()
            : null;

        var outcome = await RunRegistrationCoreAsync(request, role, auditEvent, normalizedEmail, contractorName, cancellationToken);

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
        string auditEvent,
        string normalizedEmail,
        string? contractorName,
        CancellationToken cancellationToken)
    {
        // The duplicate-name check and the seller creation/promotion must be atomic so that two
        // concurrent registrations cannot both observe the contractor name as free. Serializable
        // isolation guarantees this on relational providers; EF InMemory (integration tests) does
        // not support transactions and executes the same operations sequentially without one.
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        // Re-registering an e-mail that is already a seller must surface the "account exists" error,
        // not a spurious contractor-name conflict against its own FullName. The existing-user check
        // therefore runs before the name check, which only applies when promoting a non-seller account
        // or creating a brand-new seller.
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
                    AuditDescription: $"Registration failed for {normalizedEmail}: user already exists.",
                    EnqueueConfirmationUserId: null);
            }

            // Promotion path: a non-seller account is being promoted. Ownership of the existing
            // account must be proven first: the supplied password has to match the account being
            // promoted. Without this check anyone who learns an e-mail could attach the Seller role
            // and overwrite the contractor name, taking over the account. The failure is generic so
            // it does not reveal whether the password or the account was the problem.
            var promotionPasswordValid = await _userManager.CheckPasswordAsync(userAlreadyExists, request.Password);
            if (!promotionPasswordValid)
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
                    AuditDescription: $"Registration failed for {normalizedEmail}: existing account password mismatch.",
                    EnqueueConfirmationUserId: null);
            }

            // The contractor name is still checked against other sellers before the role is added,
            // and the FullName claim is persisted before the role so a failed claim never leaves a
            // Seller role without FullName on a non-transactional provider (InMemory). Every
            // IdentityResult is validated.
            if (contractorName is not null)
            {
                var nameAlreadyRegistered = await IsContractorNameRegisteredBySellerAsync(contractorName, cancellationToken);
                if (nameAlreadyRegistered)
                {
                    Log.Warning("{EventType} | Registration failed | ContractorName={FullName}", "USER_REGISTER_FAILED", contractorName);

                    return new RegistrationOutcome(
                        Result: new RegistrationResultDto
                        {
                            Succeeded = false,
                            ContractorNameAlreadyRegistered = true,
                            Errors = ["Nome do contratante já cadastrado."]
                        },
                        AuditUserId: userAlreadyExists.Id,
                        AuditEvent: $"{auditEvent}Failed",
                        AuditDescription: $"Registration failed for {normalizedEmail}: contractor name already registered.",
                        EnqueueConfirmationUserId: null);
                }

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

            var addRoleResult = await _userManager.AddToRoleAsync(userAlreadyExists, role);
            if (!addRoleResult.Succeeded)
            {
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

            Log.Information("{EventType} | Added {Role} role to existing user | Email={Email}", auditEvent, role, normalizedEmail);

            var confirmationPending = !userAlreadyExists.EmailConfirmed;

            return new RegistrationOutcome(
                Result: new RegistrationResultDto
                {
                    Succeeded = true,
                    UserId = userAlreadyExists.Id,
                    EmailConfirmationPending = confirmationPending,
                    // A confirmed account has no pending e-mail to change; issuing a challenge there
                    // would hand out an unnecessary credential.
                    EmailChangeChallenge = confirmationPending
                        ? await BuildEmailChangeChallengeAsync(userAlreadyExists, normalizedEmail)
                        : null
                },
                AuditUserId: null,
                AuditEvent: null,
                AuditDescription: null,
                EnqueueConfirmationUserId: confirmationPending ? userAlreadyExists.Id : null);
        }

        if (contractorName is not null)
        {
            var nameAlreadyRegistered = await IsContractorNameRegisteredBySellerAsync(contractorName, cancellationToken);
            if (nameAlreadyRegistered)
            {
                Log.Warning("{EventType} | Registration failed | ContractorName={FullName}", "USER_REGISTER_FAILED", contractorName);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = false,
                        ContractorNameAlreadyRegistered = true,
                        Errors = ["Nome do contratante já cadastrado."]
                    },
                    AuditUserId: null,
                    AuditEvent: $"{auditEvent}Failed",
                    AuditDescription: $"Registration failed for {normalizedEmail}: contractor name already registered.",
                    EnqueueConfirmationUserId: null);
            }
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
                    // validates the existing (unconfirmed) account. Otherwise the flow still resends
                    // the confirmation e-mail to the real owner but never exposes a challenge.
                    var pendingOwnerPasswordValid = await _userManager.CheckPasswordAsync(existingUser, request.Password);

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
            var documentClaimResult = await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("Document", cleanDoc));
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
            var fullNameClaimResult = await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FullName", request.FullName.Trim()));
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

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            Log.Warning("{EventType} | Registration failed (role) | Email={Email} | Errors={Errors}", "USER_REGISTER_FAILED", normalizedEmail, string.Join("; ", roleResult.Errors.Select(x => x.Description)));
            var errors = roleResult.Errors.Select(x => x.Description).ToArray();
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
                EmailChangeChallenge = await BuildEmailChangeChallengeAsync(user, normalizedEmail)
            },
            AuditUserId: user.Id,
            AuditEvent: auditEvent,
            AuditDescription: $"User {request.FullName} registered as {role} ({normalizedEmail}).",
            EnqueueConfirmationUserId: user.Id);
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
            return await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("FullName", normalizedFullName));
        }

        if (string.Equals(existingClaim.Value, normalizedFullName, StringComparison.Ordinal))
        {
            return IdentityResult.Success;
        }

        return await _userManager.ReplaceClaimAsync(user, existingClaim, new System.Security.Claims.Claim("FullName", normalizedFullName));
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

    private async Task<bool> IsContractorNameRegisteredBySellerAsync(string contractorName, CancellationToken cancellationToken)
    {
        // Single relational query joining UserClaims -> UserRoles -> Roles filtered by the Seller
        // role. EF translates ToUpper() to UPPER() so the comparison is case-insensitive and runs in
        // the database instead of loading every seller name into memory.
        return await (
            from claim in _dbContext.UserClaims
            join userRole in _dbContext.UserRoles on claim.UserId equals userRole.UserId
            join role in _dbContext.Roles on userRole.RoleId equals role.Id
            where role.Name == SellerRole
                && claim.ClaimType == "FullName"
                && claim.ClaimValue != null
                && claim.ClaimValue.ToUpper() == contractorName.ToUpper()
            select claim.Id
        ).AnyAsync(cancellationToken);
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

        // A password reset invalidates every existing session: active refresh tokens for the user
        // are revoked so a stolen/old refresh cookie cannot mint new access tokens after recovery.
        await _refreshTokenRepository.RevokeAllForUserAsync(matchedUser.Id, DateTime.UtcNow, cancellationToken);
        await _efUnitOfWork.SaveChangesAsync(cancellationToken);

        Log.Information("{EventType} | Password reset completed | UserId={UserId}", "PASSWORD_RESET_COMPLETED", matchedUser.Id);
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
