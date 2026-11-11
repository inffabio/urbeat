using System.Security.Claims;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private readonly IRolePromotionChallengeService _rolePromotionChallengeService;
    private readonly ITokenHasher _tokenHasher;
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
        IRolePromotionChallengeService rolePromotionChallengeService,
        ITokenHasher tokenHasher,
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
        _rolePromotionChallengeService = rolePromotionChallengeService;
        _tokenHasher = tokenHasher;
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
            if (outcome.Result.Succeeded)
            {
                // The registration/promotion is already committed at this point. An audit failure
                // must not turn the successful operation into an error for the caller.
                await TryWriteAuditLogAfterCommitAsync(
                    userId: outcome.AuditUserId,
                    auditEvent: outcome.AuditEvent,
                    entity: nameof(IdentityUser<Guid>),
                    entityId: outcome.AuditUserId,
                    description: outcome.AuditDescription!,
                    cancellationToken: cancellationToken);
            }
            else
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
        }

        if (outcome.EnqueueConfirmationUserId is { } enqueueUserId)
        {
            _backgroundJobClient.Enqueue<SendEmailConfirmationJob>(job => job.ExecuteAsync(enqueueUserId));
        }

        if (outcome.EnqueueRolePromotionRequestId is { } rolePromotionRequestId)
        {
            // The durable payload is only the request id. The token is re-derived from the persisted
            // request by the job, and the recurring recovery sweep re-enqueues any pending request
            // whose delivery was lost between the commit and this call.
            _backgroundJobClient.Enqueue<SendRolePromotionEmailJob>(job => job.ExecuteAsync(rolePromotionRequestId));
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

            // Promotion path. A bearer token for this very identity is the only immediate proof of
            // ownership. Knowing the e-mail (or the old CurrentPassword compatibility field) is
            // never enough: an anonymous request only creates a pending e-mail challenge, and the
            // role/credential are attached after the owner confirms the link.
            var isAuthenticatedOwner = authenticatedUserId == userAlreadyExists.Id
                && authenticatedRoles is { Count: > 0 };

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

            var existingCredential = await _dbContext.UserRoleCredentials
                .SingleOrDefaultAsync(x => x.UserId == userAlreadyExists.Id && x.Role == credentialRole, cancellationToken);
            if (existingCredential is not null)
            {
                // The role is missing (checked above), so this credential is a leftover from an
                // interrupted promotion. It is never overwritten and never adopted implicitly: only
                // the pending request that produced it can complete it, and doing so still requires
                // the single-use e-mail link (proving mailbox control) for the same request.
                var linkedRequestIsActive = existingCredential.RolePromotionRequestId is { } linkedRequestId
                    && await _dbContext.RolePromotionRequests
                        .AsNoTracking()
                        .AnyAsync(x => x.Id == linkedRequestId
                                       && x.UserId == userAlreadyExists.Id
                                       && x.TargetRole == credentialRole
                                       && x.ConsumedAtUtc == null
                                       && x.SupersededAtUtc == null
                                       && x.ExpiresAtUtc > DateTime.UtcNow,
                            cancellationToken);

                if (!linkedRequestIsActive)
                {
                    // Ambiguous origin: never overwrite the credential nor claim it for a new
                    // promotion. The failure is audited so the leftover can be remediated explicitly
                    // (support/ops) without granting a role through this path.
                    Log.Warning("{EventType} | Registration failed (unlinked credential) | Email={Email} | Role={Role}",
                        "USER_REGISTER_FAILED", normalizedEmail, role);

                    return new RegistrationOutcome(
                        Result: new RegistrationResultDto
                        {
                            Succeeded = false,
                            Errors = ["An account with this e-mail already exists."]
                        },
                        AuditUserId: userAlreadyExists.Id,
                        AuditEvent: $"{auditEvent}Failed",
                        AuditDescription: $"Registration failed for {normalizedEmail}: a {role} credential exists without a link to a pending promotion request; manual remediation is required.",
                        EnqueueConfirmationUserId: null);
                }

                // Reuse the still-pending request that owns the credential. No new e-mail is
                // enqueued when the existing link was already delivered; confirming it completes the
                // promotion by reusing the credential instead of duplicating it.
                var resumedRequest = await _rolePromotionChallengeService.CreateAsync(
                    new RolePromotionRequestData
                    {
                        UserId = userAlreadyExists.Id,
                        Email = normalizedEmail,
                        TargetRole = credentialRole,
                        PasswordHash = HashPassword(userAlreadyExists, request.Password),
                        FullName = contractorName
                    },
                    cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                Log.Information(
                    "{EventType} | Role promotion resumed for an interrupted credential | RequestId={RequestId} | UserId={UserId} | Role={Role}",
                    "ROLE_PROMOTION_RESUMED",
                    resumedRequest.RequestId,
                    userAlreadyExists.Id,
                    role);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = true,
                        // Same generic response as a normal promotion: an anonymous caller learns
                        // nothing about the identity state.
                        UserId = null,
                        EmailConfirmationPending = false,
                        RoleAddedToExistingAccount = false,
                        RolePromotionPending = true
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}PromotionPending",
                    AuditDescription: $"{role} promotion for existing identity {normalizedEmail} was resumed from a credential left by an interrupted attempt; the role remains pending until the existing e-mail challenge is confirmed.",
                    EnqueueConfirmationUserId: null,
                    EnqueueRolePromotionRequestId: resumedRequest.EmailDeliveryPending
                        ? resumedRequest.RequestId
                        : null);
            }

            if (!isAuthenticatedOwner)
            {
                // Never attach the role or the new credential based on the e-mail alone. The
                // password is hashed before persistence; the raw value is discarded with the
                // request object. The response is intentionally generic about the other role.
                var promotionRequest = await _rolePromotionChallengeService.CreateAsync(
                    new RolePromotionRequestData
                    {
                        UserId = userAlreadyExists.Id,
                        Email = normalizedEmail,
                        TargetRole = credentialRole,
                        PasswordHash = HashPassword(userAlreadyExists, request.Password),
                        FullName = contractorName
                    },
                    cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                Log.Information(
                    "{EventType} | Role promotion pending e-mail confirmation | RequestId={RequestId} | UserId={UserId} | Role={Role} | Reused={Reused} | Suppressed={Suppressed}",
                    "ROLE_PROMOTION_PENDING",
                    promotionRequest.RequestId,
                    userAlreadyExists.Id,
                    role,
                    promotionRequest.ReusedExisting,
                    promotionRequest.DeliverySuppressed);

                return new RegistrationOutcome(
                    Result: new RegistrationResultDto
                    {
                        Succeeded = true,
                        // Do not hand the existing identity id or its confirmation state to an
                        // anonymous requester: the response only states that a challenge is pending.
                        UserId = null,
                        EmailConfirmationPending = false,
                        RoleAddedToExistingAccount = false,
                        RolePromotionPending = true
                    },
                    AuditUserId: userAlreadyExists.Id,
                    AuditEvent: $"{auditEvent}PromotionPending",
                    AuditDescription: $"{role} promotion requested for existing identity {normalizedEmail}; role and credential remain pending until the e-mail challenge is confirmed.",
                    EnqueueConfirmationUserId: null,
                    EnqueueRolePromotionRequestId: promotionRequest.EmailDeliveryPending
                        ? promotionRequest.RequestId
                        : null);
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
        Guid? EnqueueConfirmationUserId,
        Guid? EnqueueRolePromotionRequestId = null);

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
        var tokenHash = _tokenHasher.Hash(rawToken);
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
            if (_tokenHasher.Verify(token, entity.TokenHash))
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
            if (_tokenHasher.Verify(request.Token, entity.TokenHash))
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

    public async Task<ConfirmRolePromotionResultDto> ConfirmRolePromotionAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var validation = await _rolePromotionChallengeService.ValidateAndConsumeAsync(token, cancellationToken);
        if (!validation.IsValid)
        {
            var activeTransaction = transaction;

            if (validation.Status == RolePromotionChallengeStatus.AlreadyConsumed)
            {
                // The consume attempt lost a concurrency race or replayed an old link. Roll back
                // before querying: a failed PostgreSQL statement aborts the transaction, so the
                // idempotency check must run outside it.
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    activeTransaction = null;
                }

                // A concurrency conflict leaves the losing entity marked as modified. Clear it so
                // the idempotency query and the audit insert cannot retry the aborted consume.
                _dbContext.ChangeTracker.Clear();

                var replay = await BuildAlreadyAppliedPromotionResultAsync(validation.Request, cancellationToken);
                if (replay is not null)
                {
                    Log.Information(
                        "{EventType} | Role promotion replay is idempotent | UserId={UserId} | Role={Role}",
                        "ROLE_PROMOTION_REPLAY",
                        validation.Request?.UserId,
                        replay.Role);
                    await TryWriteAuditLogAfterCommitAsync(
                        validation.Request?.UserId,
                        "RolePromotionReplay",
                        nameof(RolePromotionRequest),
                        validation.Request?.Id,
                        $"Role promotion confirmation replayed for {validation.Request?.NormalizedEmail}; the existing role and credential were preserved.",
                        cancellationToken);

                    return new ConfirmRolePromotionResultDto
                    {
                        Succeeded = true,
                        AlreadyApplied = true,
                        Role = replay.Role,
                        EmailConfirmationPending = replay.EmailConfirmationPending
                    };
                }
            }

            return await FailRolePromotionAsync(
                activeTransaction,
                validation.Request?.UserId,
                validation.Request?.NormalizedEmail,
                $"challenge rejected with status {validation.Status}",
                validation.ClaimAttemptId,
                validation.Request?.Id ?? Guid.Empty,
                cancellationToken);
        }

        var request = validation.Request!;
        var claimAttemptId = validation.ClaimAttemptId;
        if (claimAttemptId is null || claimAttemptId == Guid.Empty)
        {
            // A valid challenge always carries the attempt that owns the lease. Without it the
            // request must not be applied, because it could close a challenge another attempt owns.
            return await FailRolePromotionAsync(
                transaction,
                userId: request.UserId,
                email: request.NormalizedEmail,
                reason: "the challenge claim attempt is missing",
                claimAttemptId: null,
                claimRequestId: request.Id,
                cancellationToken);
        }

        var roleName = AccountRoles.ToRoleName(request.TargetRole);
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null
            || !string.Equals(
                (user.Email ?? string.Empty).Trim().ToLowerInvariant(),
                request.NormalizedEmail,
                StringComparison.Ordinal))
        {
            return await FailRolePromotionAsync(
                transaction,
                request.UserId,
                request.NormalizedEmail,
                "identity or e-mail no longer matches the pending request",
                claimAttemptId,
                request.Id,
                cancellationToken);
        }

        var roleAlreadyExists = await _userManager.IsInRoleAsync(user, roleName);
        var existingCredential = await _dbContext.UserRoleCredentials
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.Role == request.TargetRole, cancellationToken);

        if (roleAlreadyExists && existingCredential is not null)
        {
            var emailConfirmedChanged = false;
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                emailConfirmedChanged = true;
                var confirmResult = await _userManager.UpdateAsync(user);
                if (!confirmResult.Succeeded)
                {
                    return await FailRolePromotionAsync(
                        transaction,
                        user.Id,
                        request.NormalizedEmail,
                        $"e-mail confirmation update failed: {string.Join("; ", confirmResult.Errors.Select(x => x.Description))}",
                        claimAttemptId,
                        request.Id,
                        cancellationToken);
                }
            }

            // The role and credential already exist (a previous attempt crashed before the request
            // was marked). Record the application so the request stops being the active pending one;
            // no duplicate credential is created.
            var applied = await _rolePromotionChallengeService.MarkAppliedAsync(
                request.Id,
                claimAttemptId.Value,
                cancellationToken);

            if (!applied)
            {
                // The lease expired or another attempt took over: never report success. The e-mail
                // confirmation flag set above is reverted on providers without a transaction; the
                // role/credential were not written by this attempt, so they are left untouched.
                if (emailConfirmedChanged)
                {
                    await CompensateFailedRolePromotionAsync(
                        transaction,
                        new RolePromotionCompensationPlan(
                            RequestId: request.Id,
                            UserId: user.Id,
                            TargetRole: request.TargetRole,
                            CredentialId: null,
                            RoleName: roleName,
                            RoleAssignedByThisAttempt: false,
                            PreviousFullNameClaim: null,
                            FullNameChanged: false,
                            AppliedFullName: null,
                            EmailConfirmedAssignedByThisAttempt: true),
                        request.NormalizedEmail,
                        cancellationToken);
                }

                return await FailRolePromotionAsync(
                    transaction,
                    user.Id,
                    request.NormalizedEmail,
                    "the confirmation lease is no longer valid",
                    claimAttemptId,
                    request.Id,
                    cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await TryWriteAuditLogAfterCommitAsync(
                user.Id,
                "RolePromotionReplay",
                nameof(RolePromotionRequest),
                request.Id,
                $"Role promotion already applied for {request.NormalizedEmail} as {roleName}; no duplicate credential was created.",
                cancellationToken);

            return new ConfirmRolePromotionResultDto
            {
                Succeeded = true,
                AlreadyApplied = true,
                Role = roleName,
                EmailConfirmationPending = false
            };
        }

        // A pending request only exists while the identity owns neither the role nor the credential.
        // Finding the role without the credential therefore means an earlier attempt of this very
        // request wrote the role and could not compensate it (the credential is compensated first).
        // The owner is confirming the same request again, so the attempt is completed by reusing the
        // leftover role instead of rejecting the account forever with an "inconsistent state" error.
        var roleLeftoverFromEarlierAttempt = roleAlreadyExists;

        // The mirror case: the credential was written and the role assignment failed. Only a
        // credential linked to this exact request belongs to this promotion and may be reused; any
        // other credential has an ambiguous origin and must never be adopted or overwritten.
        var credentialLeftoverFromEarlierAttempt = existingCredential is not null;
        if (credentialLeftoverFromEarlierAttempt
            && existingCredential!.RolePromotionRequestId != request.Id)
        {
            return await FailRolePromotionAsync(
                transaction,
                user.Id,
                request.NormalizedEmail,
                "a credential for the target role exists without a link to this request; manual remediation is required",
                claimAttemptId,
                request.Id,
                cancellationToken);
        }

        if (!credentialLeftoverFromEarlierAttempt && string.IsNullOrWhiteSpace(request.PasswordHash))
        {
            // Corrupt pending data: never create a credential with an empty password.
            return await FailRolePromotionAsync(
                transaction,
                user.Id,
                request.NormalizedEmail,
                "the stored password hash is empty",
                claimAttemptId,
                request.Id,
                cancellationToken);
        }

        Claim? previousFullNameClaim = null;
        var fullNameChanged = false;
        string? appliedFullName = null;
        Guid? createdCredentialId = null;
        if (request.TargetRole == UserRoleType.Seller && !string.IsNullOrWhiteSpace(request.FullName))
        {
            var normalizedFullName = request.FullName.Trim();
            previousFullNameClaim = (await _userManager.GetClaimsAsync(user))
                .FirstOrDefault(claim => claim.Type == "FullName");
            fullNameChanged = !string.Equals(
                previousFullNameClaim?.Value,
                normalizedFullName,
                StringComparison.Ordinal);
            if (fullNameChanged)
            {
                appliedFullName = normalizedFullName;
            }

            var claimResult = await UpsertFullNameClaimAsync(user, normalizedFullName);
            if (!claimResult.Succeeded)
            {
                return await FailRolePromotionAsync(
                    transaction,
                    user.Id,
                    request.NormalizedEmail,
                    $"FullName claim update failed: {string.Join("; ", claimResult.Errors.Select(x => x.Description))}",
                    claimAttemptId,
                    request.Id,
                    cancellationToken);
            }
        }

        if (!credentialLeftoverFromEarlierAttempt)
        {
            var credential = new UserRoleCredential
            {
                UserId = user.Id,
                Role = request.TargetRole,
                PasswordHash = request.PasswordHash,
                AccessFailedCount = 0,
                LockoutEndUtc = null,
                // The link is what makes a retry after a failed compensation recoverable: only the
                // request that created the credential may finish it by reusing this row.
                RolePromotionRequestId = request.Id
            };

            _dbContext.UserRoleCredentials.Add(credential);
            createdCredentialId = credential.Id;

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception)
            {
                // The claim may already be persisted on a provider without transactions. Compensate
                // it (and detach the failed credential) before surfacing the failure; PostgreSQL
                // rolls the whole transaction back instead.
                await CompensateFailedRolePromotionAsync(
                    transaction,
                    new RolePromotionCompensationPlan(
                        RequestId: request.Id,
                        UserId: user.Id,
                        TargetRole: request.TargetRole,
                        CredentialId: createdCredentialId,
                        RoleName: roleName,
                        RoleAssignedByThisAttempt: false,
                        PreviousFullNameClaim: previousFullNameClaim,
                        FullNameChanged: fullNameChanged,
                        AppliedFullName: appliedFullName,
                        EmailConfirmedAssignedByThisAttempt: false),
                    request.NormalizedEmail,
                    cancellationToken);

                if (transaction is null)
                {
                    await ReleaseClaimBestEffortAsync(request.Id, claimAttemptId.Value, cancellationToken);
                }
                throw;
            }
        }

        if (!roleLeftoverFromEarlierAttempt)
        {
            IdentityResult addRoleResult;
            try
            {
                addRoleResult = await _userManager.AddToRoleAsync(user, roleName);
            }
            catch (Exception)
            {
                // A persistence exception (as opposed to a validation failure) must also undo the
                // credential and claim already written. The role is not removed here: the exception
                // does not prove this attempt wrote it, and a credential from another operation must
                // never be touched.
                await CompensateFailedRolePromotionAsync(
                    transaction,
                    new RolePromotionCompensationPlan(
                        RequestId: request.Id,
                        UserId: user.Id,
                        TargetRole: request.TargetRole,
                        CredentialId: createdCredentialId,
                        RoleName: roleName,
                        RoleAssignedByThisAttempt: false,
                        PreviousFullNameClaim: previousFullNameClaim,
                        FullNameChanged: fullNameChanged,
                        AppliedFullName: appliedFullName,
                        EmailConfirmedAssignedByThisAttempt: false),
                    request.NormalizedEmail,
                    cancellationToken);

                if (transaction is null)
                {
                    await ReleaseClaimBestEffortAsync(request.Id, claimAttemptId.Value, cancellationToken);
                }
                throw;
            }

            if (!addRoleResult.Succeeded)
            {
                // Undo the credential and the claim too, so the retry is not a partial apply. The
                // role assignment failed, so this attempt did not create a role to remove.
                await CompensateFailedRolePromotionAsync(
                    transaction,
                    new RolePromotionCompensationPlan(
                        RequestId: request.Id,
                        UserId: user.Id,
                        TargetRole: request.TargetRole,
                        CredentialId: createdCredentialId,
                        RoleName: roleName,
                        RoleAssignedByThisAttempt: false,
                        PreviousFullNameClaim: previousFullNameClaim,
                        FullNameChanged: fullNameChanged,
                        AppliedFullName: appliedFullName,
                        EmailConfirmedAssignedByThisAttempt: false),
                    request.NormalizedEmail,
                    cancellationToken);

                return await FailRolePromotionAsync(
                    transaction,
                    user.Id,
                    request.NormalizedEmail,
                    $"role assignment failed: {string.Join("; ", addRoleResult.Errors.Select(x => x.Description))}",
                    claimAttemptId,
                    request.Id,
                    cancellationToken);
            }
        }

        // Clicking the single-use link delivered to the address proves control of that mailbox, so
        // an identity that was still pending confirmation becomes usable in the same transaction.
        var emailConfirmedAssignedByThisAttempt = false;
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            emailConfirmedAssignedByThisAttempt = true;

            IdentityResult confirmResult;
            try
            {
                confirmResult = await _userManager.UpdateAsync(user);
            }
            catch (Exception)
            {
                await CompensateFailedRolePromotionAsync(
                    transaction,
                    new RolePromotionCompensationPlan(
                        RequestId: request.Id,
                        UserId: user.Id,
                        TargetRole: request.TargetRole,
                        CredentialId: createdCredentialId,
                        RoleName: roleName,
                        RoleAssignedByThisAttempt: !roleLeftoverFromEarlierAttempt,
                        PreviousFullNameClaim: previousFullNameClaim,
                        FullNameChanged: fullNameChanged,
                        AppliedFullName: appliedFullName,
                        EmailConfirmedAssignedByThisAttempt: emailConfirmedAssignedByThisAttempt),
                    request.NormalizedEmail,
                    cancellationToken);

                if (transaction is null)
                {
                    await ReleaseClaimBestEffortAsync(request.Id, claimAttemptId.Value, cancellationToken);
                }
                throw;
            }

            if (!confirmResult.Succeeded)
            {
                // The role and credential were already written before this step. Without a
                // transaction they must be undone explicitly, together with the claim and the
                // e-mail confirmation flag, otherwise a retry would start from a half-applied state.
                await CompensateFailedRolePromotionAsync(
                    transaction,
                    new RolePromotionCompensationPlan(
                        RequestId: request.Id,
                        UserId: user.Id,
                        TargetRole: request.TargetRole,
                        CredentialId: createdCredentialId,
                        RoleName: roleName,
                        RoleAssignedByThisAttempt: !roleLeftoverFromEarlierAttempt,
                        PreviousFullNameClaim: previousFullNameClaim,
                        FullNameChanged: fullNameChanged,
                        AppliedFullName: appliedFullName,
                        EmailConfirmedAssignedByThisAttempt: emailConfirmedAssignedByThisAttempt),
                    request.NormalizedEmail,
                    cancellationToken);

                return await FailRolePromotionAsync(
                    transaction,
                    user.Id,
                    request.NormalizedEmail,
                    $"e-mail confirmation update failed: {string.Join("; ", confirmResult.Errors.Select(x => x.Description))}",
                    claimAttemptId,
                    request.Id,
                    cancellationToken);
            }
        }

        // Only now, after the role, claims and independent credential are persisted, the challenge
        // is marked as applied. A failure above rolls the attempt back (PostgreSQL) or leaves the
        // request untouched (InMemory), so the owner can retry the same link without duplicating
        // anything; the checks above make a later replay idempotent. The marker is conditional on
        // this attempt still holding the lease: an expired/taken-over lease must never be closed by
        // a caller that lost it.
        var markedApplied = await _rolePromotionChallengeService.MarkAppliedAsync(
            request.Id,
            claimAttemptId.Value,
            cancellationToken);

        if (!markedApplied)
        {
            // Do not compensate the role/credential here: another attempt may own them now. The
            // transaction rollback removes this attempt's writes on PostgreSQL; on a provider
            // without transactions the idempotent retry closes the request. The response is an error.
            return await FailRolePromotionAsync(
                transaction,
                user.Id,
                request.NormalizedEmail,
                "the confirmation lease is no longer valid",
                claimAttemptId,
                request.Id,
                cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        Log.Information(
            "{EventType} | Role promotion confirmed | UserId={UserId} | Role={Role}",
            "ROLE_PROMOTION_CONFIRMED",
            user.Id,
            roleName);
        await TryWriteAuditLogAfterCommitAsync(
            user.Id,
            "RolePromotionConfirmed",
            nameof(RolePromotionRequest),
            request.Id,
            $"{roleName} access and an independent credential were added to {request.NormalizedEmail} after e-mail confirmation.",
            cancellationToken);

        return new ConfirmRolePromotionResultDto
        {
            Succeeded = true,
            AlreadyApplied = false,
            Role = roleName,
            EmailConfirmationPending = false
        };
    }

    private async Task<ConfirmRolePromotionResultDto?> BuildAlreadyAppliedPromotionResultAsync(
        RolePromotionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null
            || !string.Equals(
                (user.Email ?? string.Empty).Trim().ToLowerInvariant(),
                request.NormalizedEmail,
                StringComparison.Ordinal))
        {
            return null;
        }

        var roleName = AccountRoles.ToRoleName(request.TargetRole);
        if (!await _userManager.IsInRoleAsync(user, roleName))
        {
            return null;
        }

        var credentialExists = await _dbContext.UserRoleCredentials
            .AnyAsync(x => x.UserId == user.Id && x.Role == request.TargetRole, cancellationToken);
        if (!credentialExists)
        {
            return null;
        }

        return new ConfirmRolePromotionResultDto
        {
            Succeeded = true,
            AlreadyApplied = true,
            Role = roleName,
            EmailConfirmationPending = !user.EmailConfirmed
        };
    }

    private async Task<ConfirmRolePromotionResultDto> FailRolePromotionAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        Guid? userId,
        string? email,
        string reason,
        Guid? claimAttemptId,
        Guid claimRequestId,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        // Rollback leaves the tracked entities in their modified state; clearing prevents the audit
        // insert from accidentally re-persisting a consumed challenge or a half-applied credential.
        _dbContext.ChangeTracker.Clear();

        if (transaction is null && claimAttemptId is { } attemptId && attemptId != Guid.Empty)
        {
            // On PostgreSQL the rollback already removed the lease taken inside the transaction. On
            // a provider without transactions the lease is persisted, so it must be released
            // explicitly; otherwise a failure would block an immediate retry until the lease expires.
            await ReleaseClaimBestEffortAsync(claimRequestId, attemptId, cancellationToken);
        }

        Log.Warning(
            "{EventType} | Role promotion confirmation failed | UserId={UserId} | Email={Email} | Reason={Reason}",
            "ROLE_PROMOTION_CONFIRMATION_FAILED",
            userId,
            email,
            reason);

        await WriteAuditLogAsync(
            userId,
            "RolePromotionConfirmationFailed",
            nameof(RolePromotionRequest),
            entityId: null,
            description: $"Role promotion confirmation failed: {reason}.",
            ipAddress: null,
            cancellationToken);

        return new ConfirmRolePromotionResultDto
        {
            Succeeded = false,
            Error = "Link de confirmação inválido ou expirado."
        };
    }

    private async Task ReleaseClaimBestEffortAsync(
        Guid requestId,
        Guid claimAttemptId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _rolePromotionChallengeService.ReleaseClaimAsync(
                requestId,
                claimAttemptId,
                cancellationToken);
        }
        catch (Exception releaseException)
        {
            // The original failure (or the rejection) is what the caller must see. A lease that
            // could not be released expires on its own, so recovery is still possible.
            Log.Warning(
                releaseException,
                "{EventType} | Could not release the role promotion claim | RequestId={RequestId}",
                "ROLE_PROMOTION_CLAIM_RELEASE_FAILED",
                requestId);
        }
    }

    /// <summary>
    /// Writes an audit entry for an operation that already succeeded (and, for relational
    /// deployments, already committed). A failure here must not turn a successful promotion into an
    /// error for the owner: the security-relevant state is already persisted, so the audit failure is
    /// logged for retry/outbox follow-up instead of being surfaced. The token and password are never
    /// part of the description.
    /// </summary>
    private async Task TryWriteAuditLogAfterCommitAsync(
        Guid? userId,
        string auditEvent,
        string entity,
        Guid? entityId,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteAuditLogAsync(
                userId,
                auditEvent,
                entity,
                entityId,
                description,
                ipAddress: null,
                cancellationToken);
        }
        catch (Exception auditException)
        {
            Log.Warning(
                auditException,
                "{EventType} | Post-commit audit write failed | Event={AuditEvent} | UserId={UserId} | EntityId={EntityId}",
                "ROLE_PROMOTION_AUDIT_WRITE_FAILED",
                auditEvent,
                userId,
                entityId);
        }
    }

    /// <summary>
    /// Compensates a failed attempt outside a database transaction and reports when the compensation
    /// itself could not be completed. PostgreSQL rolls the attempt back, so there is nothing to
    /// compensate there. An incomplete compensation is never turned into a success: the caller still
    /// fails the request, the failure is audited, and the leftover state is repaired by the next
    /// confirmation attempt. No password, token or e-mail content is logged.
    /// </summary>
    private async Task CompensateFailedRolePromotionAsync(
        IDbContextTransaction? transaction,
        RolePromotionCompensationPlan plan,
        string? normalizedEmail,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            return;
        }

        var compensation = await TryCompensateRolePromotionAsync(plan, cancellationToken);

        if (compensation.Succeeded)
        {
            return;
        }

        Log.Error(
            "{EventType} | Role promotion compensation is incomplete | UserId={UserId} | Role={Role} | FailedSteps={FailedSteps}",
            "ROLE_PROMOTION_COMPENSATION_FAILED",
            plan.UserId,
            plan.RoleName,
            string.Join(", ", compensation.FailedSteps));

        try
        {
            await WriteAuditLogAsync(
                plan.UserId,
                "RolePromotionCompensationFailed",
                nameof(RolePromotionRequest),
                entityId: null,
                $"Role promotion compensation for {normalizedEmail} as {plan.RoleName} could not complete ({string.Join(", ", compensation.FailedSteps)}); the next confirmation attempt repairs the leftover state.",
                ipAddress: null,
                cancellationToken);
        }
        catch (Exception auditException)
        {
            // The original failure is what the caller must see; the audit write never masks it.
            Log.Warning(
                auditException,
                "{EventType} | Role promotion compensation audit failed | UserId={UserId}",
                "ROLE_PROMOTION_COMPENSATION_AUDIT_FAILED",
                plan.UserId);
        }
    }

    /// <summary>
    /// Reverts only the effects this attempt provably created: the credential row identified by the
    /// id generated for this attempt and linked to this request, the role only when this attempt
    /// assigned it, and the claim/confirmation flag only when this attempt changed them. A credential
    /// or role created by a concurrent registration is never removed or overwritten. Every step
    /// result is checked and a failure aborts the remaining steps, so the caller knows the
    /// compensation is incomplete instead of assuming a rollback. The credential is removed before
    /// the role, because a failure in the middle must never leave a credential without its role.
    /// </summary>
    private async Task<RolePromotionCompensationResult> TryCompensateRolePromotionAsync(
        RolePromotionCompensationPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            if (plan.CredentialId is { } credentialId)
            {
                var credentialFailure = await ExecuteCompensationStepAsync("Credential", async () =>
                {
                    // The id, the owner, the role and the request link are all required. A
                    // credential created by another operation (different id, no link) is never
                    // selected, even if it targets the same role.
                    var storedCredential = await _dbContext.UserRoleCredentials
                        .SingleOrDefaultAsync(
                            x => x.Id == credentialId
                                 && x.UserId == plan.UserId
                                 && x.Role == plan.TargetRole
                                 && x.RolePromotionRequestId == plan.RequestId,
                            cancellationToken);

                    if (storedCredential is null)
                    {
                        return IdentityResult.Success;
                    }

                    _dbContext.UserRoleCredentials.Remove(storedCredential);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return IdentityResult.Success;
                });
                if (credentialFailure is not null)
                {
                    return new RolePromotionCompensationResult(false, [credentialFailure]);
                }
            }

            if (plan.RoleAssignedByThisAttempt)
            {
                var roleFailure = await ExecuteCompensationStepAsync("Role", async () =>
                {
                    var current = await _userManager.FindByIdAsync(plan.UserId.ToString());
                    if (current is null || !await _userManager.IsInRoleAsync(current, plan.RoleName))
                    {
                        return IdentityResult.Success;
                    }

                    return await _userManager.RemoveFromRoleAsync(current, plan.RoleName);
                });
                if (roleFailure is not null)
                {
                    return new RolePromotionCompensationResult(false, [roleFailure]);
                }
            }

            if (plan.FullNameChanged)
            {
                var claimFailure = await ExecuteCompensationStepAsync("FullNameClaim", async () =>
                {
                    var current = await _userManager.FindByIdAsync(plan.UserId.ToString());
                    if (current is null)
                    {
                        return IdentityResult.Success;
                    }

                    var currentClaim = (await _userManager.GetClaimsAsync(current))
                        .FirstOrDefault(claim => claim.Type == "FullName");

                    // If our write never became the stored value (or another operation replaced it),
                    // there is nothing this attempt may revert: it does not own the current claim.
                    if (plan.AppliedFullName is not null
                        && !string.Equals(currentClaim?.Value, plan.AppliedFullName, StringComparison.Ordinal))
                    {
                        return IdentityResult.Success;
                    }

                    if (plan.PreviousFullNameClaim is null)
                    {
                        return currentClaim is null
                            ? IdentityResult.Success
                            : await _userManager.RemoveClaimAsync(current, currentClaim);
                    }

                    if (currentClaim is null)
                    {
                        return await _userManager.AddClaimAsync(current, plan.PreviousFullNameClaim);
                    }

                    return string.Equals(currentClaim.Value, plan.PreviousFullNameClaim.Value, StringComparison.Ordinal)
                        ? IdentityResult.Success
                        : await _userManager.ReplaceClaimAsync(current, currentClaim, plan.PreviousFullNameClaim);
                });
                if (claimFailure is not null)
                {
                    return new RolePromotionCompensationResult(false, [claimFailure]);
                }
            }

            if (plan.EmailConfirmedAssignedByThisAttempt)
            {
                var confirmationFailure = await ExecuteCompensationStepAsync("EmailConfirmed", async () =>
                {
                    var current = await _userManager.FindByIdAsync(plan.UserId.ToString());
                    if (current is null || !current.EmailConfirmed)
                    {
                        return IdentityResult.Success;
                    }

                    current.EmailConfirmed = false;
                    return await _userManager.UpdateAsync(current);
                });
                if (confirmationFailure is not null)
                {
                    return new RolePromotionCompensationResult(false, [confirmationFailure]);
                }
            }

            var verificationFailure = await VerifyRolePromotionCompensationAsync(plan, cancellationToken);

            return verificationFailure is null
                ? RolePromotionCompensationResult.Success
                : new RolePromotionCompensationResult(false, [verificationFailure]);
        }
        catch (Exception compensationException)
        {
            Log.Warning(
                compensationException,
                "{EventType} | Role promotion compensation failed | UserId={UserId} | Role={Role}",
                "ROLE_PROMOTION_COMPENSATION_FAILED",
                plan.UserId,
                plan.RoleName);
            return new RolePromotionCompensationResult(false, ["Unexpected"]);
        }
        finally
        {
            // Never let the audit insert re-persist a compensated change.
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Runs one compensation step from a clean change tracker and converts an
    /// <see cref="IdentityResult"/> failure or a persistence exception into a reported step failure.
    /// The step name is the only detail that reaches the logs.
    /// </summary>
    private async Task<string?> ExecuteCompensationStepAsync(string step, Func<Task<IdentityResult>> action)
    {
        try
        {
            // A failed step may leave tracked entities that a later SaveChanges would re-persist.
            _dbContext.ChangeTracker.Clear();

            var result = await action();
            return result.Succeeded ? null : $"{step} ({DescribeIdentityErrors(result)})";
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "{EventType} | Role promotion compensation step failed | Step={Step}",
                "ROLE_PROMOTION_COMPENSATION_STEP_FAILED",
                step);
            return $"{step} ({exception.GetType().Name})";
        }
    }

    /// <summary>
    /// Confirms the compensated state instead of trusting the step calls. Returns the failing check
    /// name, or <see langword="null"/> when the identity no longer carries an effect of the attempt.
    /// </summary>
    private async Task<string?> VerifyRolePromotionCompensationAsync(
        RolePromotionCompensationPlan plan,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        if (plan.CredentialId is { } credentialId)
        {
            var credentialStillExists = await _dbContext.UserRoleCredentials
                .AnyAsync(
                    x => x.Id == credentialId
                         && x.UserId == plan.UserId
                         && x.Role == plan.TargetRole
                         && x.RolePromotionRequestId == plan.RequestId,
                    cancellationToken);
            if (credentialStillExists)
            {
                return "CredentialVerification";
            }
        }

        var current = await _userManager.FindByIdAsync(plan.UserId.ToString());
        if (current is null)
        {
            return null;
        }

        if (plan.RoleAssignedByThisAttempt && await _userManager.IsInRoleAsync(current, plan.RoleName))
        {
            return "RoleVerification";
        }

        if (plan.FullNameChanged)
        {
            var storedClaim = (await _userManager.GetClaimsAsync(current))
                .FirstOrDefault(claim => claim.Type == "FullName");

            // If our write never became the stored value (or another operation replaced it), this
            // attempt does not own the current claim and there is nothing to verify or revert.
            var claimNotOwnedByThisAttempt = plan.AppliedFullName is not null
                && !string.Equals(storedClaim?.Value, plan.AppliedFullName, StringComparison.Ordinal);

            if (!claimNotOwnedByThisAttempt)
            {
                var reverted = plan.PreviousFullNameClaim is null
                    ? storedClaim is null
                    : storedClaim is not null
                      && string.Equals(storedClaim.Value, plan.PreviousFullNameClaim.Value, StringComparison.Ordinal);
                if (!reverted)
                {
                    return "FullNameClaimVerification";
                }
            }
        }

        if (plan.EmailConfirmedAssignedByThisAttempt && current.EmailConfirmed)
        {
            return "EmailConfirmedVerification";
        }

        return null;
    }

    private static string DescribeIdentityErrors(IdentityResult result)
    {
        var codes = result.Errors.Select(error => error.Code).Distinct().ToArray();
        return codes.Length == 0 ? "unknown" : string.Join(", ", codes);
    }

    private sealed record RolePromotionCompensationResult(bool Succeeded, IReadOnlyList<string> FailedSteps)
    {
        public static RolePromotionCompensationResult Success { get; } = new(true, []);
    }

    /// <summary>
    /// Exactly what one confirmation attempt may undo. CredentialId is the id generated for the row
    /// this attempt inserted (never a lookup by user/role), and the role/claim/confirmation flags are
    /// true only when this attempt performed that step. A concurrent operation that created its own
    /// credential/role between the steps is therefore never touched.
    /// </summary>
    private sealed record RolePromotionCompensationPlan(
        Guid RequestId,
        Guid UserId,
        UserRoleType TargetRole,
        Guid? CredentialId,
        string RoleName,
        bool RoleAssignedByThisAttempt,
        Claim? PreviousFullNameClaim,
        bool FullNameChanged,
        string? AppliedFullName,
        bool EmailConfirmedAssignedByThisAttempt);

    private async Task ConsumeEmailChangeChallengeAsync(IdentityUser<Guid> user)
    {
        // Rotating the security stamp invalidates the just-used challenge (and any other outstanding
        // challenge) so it cannot be replayed.
        await _userManager.UpdateSecurityStampAsync(user);
    }
}
