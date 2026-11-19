using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Security;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Authoritative administrative account management. The actor is always resolved from the
/// authenticated claim by the controller; this service re-checks the target role, the
/// last-active-administrator invariant and every state transition. Tokens and passwords never leave
/// the server through a response, log or audit row.
/// </summary>
public sealed class AdminAccountService : IAdminAccountService
{
    private const string AdminRole = "Admin";
    private const string SellerRole = AccountRoles.Seller;

    private const string SellerEmailInUseMessage =
        "Este e-mail já pertence a uma conta de lojista. O convite de administrador não pode ser enviado.";

    // InMemory has neither transactions nor advisory locks. This process-wide lock serializes the
    // block/remove critical section so the last-active-admin invariant still holds in tests.
    private static readonly SemaphoreSlim InMemoryLastActiveLock = new(1, 1);

    private readonly UserManager<IdentityUser<Guid>> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IEfUnitOfWork _efUnitOfWork;
    private readonly IAdminInvitationService _invitationService;
    private readonly IAdminAccountReadRepository _readRepository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly AdminAccountOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdminAccountService> _logger;

    public AdminAccountService(
        UserManager<IdentityUser<Guid>> userManager,
        IRefreshTokenRepository refreshTokenRepository,
        ApplicationDbContext dbContext,
        IEfUnitOfWork efUnitOfWork,
        IAdminInvitationService invitationService,
        IAdminAccountReadRepository readRepository,
        IBackgroundJobClient backgroundJobClient,
        IOptions<AdminAccountOptions> options,
        TimeProvider timeProvider,
        ILogger<AdminAccountService> logger)
    {
        _userManager = userManager;
        _refreshTokenRepository = refreshTokenRepository;
        _dbContext = dbContext;
        _efUnitOfWork = efUnitOfWork;
        _invitationService = invitationService;
        _readRepository = readRepository;
        _backgroundJobClient = backgroundJobClient;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<PagedAdminAdministratorsDto> ListAsync(
        AdminAccountsQueryDto query,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
        => _readRepository.ListAsync(query, currentUserId, _options.MaxPageSize, cancellationToken);

    public async Task<AdminAccountOperationResult> InviteAsync(
        string email,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail))
        {
            return Failure(AdminAccountOperationStatus.InvalidEmail, "Informe um e-mail válido.");
        }

        var existing = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existing is not null)
        {
            // A store owner (Seller) cannot be turned into an administrator from this surface:
            // the invite would mix two credential owners on the same identity. Report the
            // specific conflict and leave the identity, its roles and invitations untouched.
            var existingIsSeller = await _userManager.IsInRoleAsync(existing, SellerRole);

            await WriteDeniedAuditAsync(
                actorUserId,
                existing.Id,
                existingIsSeller
                    ? AdminAccountErrorCodes.SellerEmailAlreadyInUse
                    : AdminAccountErrorCodes.EmailAlreadyInUse,
                ipAddress,
                cancellationToken);

            return existingIsSeller
                ? Failure(
                    AdminAccountOperationStatus.SellerEmailAlreadyInUse,
                    SellerEmailInUseMessage)
                : Failure(
                    AdminAccountOperationStatus.EmailAlreadyInUse,
                    "Este e-mail já está em uso.");
        }

        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
        var userCreated = false;
        IdentityUser<Guid>? user = null;

        try
        {
            user = new IdentityUser<Guid>
            {
                Id = Guid.CreateVersion7(),
                UserName = normalizedEmail,
                Email = normalizedEmail,
                EmailConfirmed = false
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                await RollBackAsync(transaction, cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var duplicate = createResult.Errors.Any(
                    error => error.Code is "DuplicateEmail" or "DuplicateUserName");
                if (duplicate)
                {
                    // The address was created between the pre-check and CreateAsync. Re-read the
                    // winner so a concurrently registered Seller still gets the specific conflict.
                    var owner = await _userManager.FindByEmailAsync(normalizedEmail);
                    var ownerIsSeller = owner is not null
                        && await _userManager.IsInRoleAsync(owner, SellerRole);

                    await WriteDeniedAuditAsync(
                        actorUserId,
                        owner?.Id,
                        ownerIsSeller
                            ? AdminAccountErrorCodes.SellerEmailAlreadyInUse
                            : AdminAccountErrorCodes.EmailAlreadyInUse,
                        ipAddress,
                        cancellationToken);

                    if (ownerIsSeller)
                    {
                        return Failure(
                            AdminAccountOperationStatus.SellerEmailAlreadyInUse,
                            SellerEmailInUseMessage);
                    }
                }

                return Failure(
                    duplicate
                        ? AdminAccountOperationStatus.EmailAlreadyInUse
                        : AdminAccountOperationStatus.Failed,
                    "Não foi possível criar o convite.");
            }

            userCreated = true;

            var roleResult = await _userManager.AddToRoleAsync(user, AdminRole);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException("Could not assign the Admin role to the invited account.");
            }

            var invitation = await _invitationService.CreateAsync(
                user.Id,
                normalizedEmail,
                AdminInvitationPurpose.AccountInvitation,
                actorUserId,
                cancellationToken);

            AddAudit(
                actorUserId,
                user.Id,
                "AdminInvitationSent",
                $"Administrative invitation sent to {normalizedEmail}; expires at {invitation.ExpiresAtUtc:O}.",
                ipAddress);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            EnqueueInvitationEmail(invitation.Id);

            _logger.LogInformation(
                "{EventType} | Administrator invited | UserId={UserId} | InvitationId={InvitationId} | IP={IpAddress}",
                "ADMIN_INVITATION_SENT",
                user.Id,
                invitation.Id,
                ipAddress);

            return new AdminAccountOperationResult
            {
                Status = AdminAccountOperationStatus.Succeeded,
                Administrator = new AdminAdministratorDto
                {
                    Id = user.Id,
                    Email = normalizedEmail,
                    Status = "Invited",
                    IsEmailConfirmed = false,
                    IsBlocked = false,
                    IsCurrentUser = false,
                    IsLastActiveAdmin = false,
                    CreatedAtUtc = invitation.CreatedAtUtc,
                    Invitation = _invitationService.ToDto(
                        invitation,
                        _timeProvider.GetUtcNow().UtcDateTime)
                },
                Invitation = _invitationService.ToDto(
                    invitation,
                    _timeProvider.GetUtcNow().UtcDateTime)
            };
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);

            if (userCreated && user is not null && !_dbContext.Database.IsRelational())
            {
                await TryDeleteInMemoryUserAsync(user);
            }

            _logger.LogError(
                exception,
                "{EventType} | Administrator invitation failed | IP={IpAddress}",
                "ADMIN_INVITATION_FAILED",
                ipAddress);
            return Failure(AdminAccountOperationStatus.Failed, "Não foi possível criar o convite.");
        }
    }

    public async Task<AdminAccountOperationResult> UpdateEmailAsync(
        Guid targetUserId,
        string email,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var target = await LoadAdministratorAsync(targetUserId, cancellationToken);
        if (target is null)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                targetUserId,
                AdminAccountErrorCodes.AdminNotFound,
                ipAddress,
                cancellationToken);
            return Failure(AdminAccountOperationStatus.NotFound, "Administrador não encontrado.");
        }

        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail))
        {
            return Failure(AdminAccountOperationStatus.InvalidEmail, "Informe um e-mail válido.");
        }

        var currentEmail = target.Email ?? string.Empty;
        if (string.Equals(NormalizeEmail(currentEmail), normalizedEmail, StringComparison.Ordinal))
        {
            return new AdminAccountOperationResult
            {
                Status = AdminAccountOperationStatus.Succeeded,
                Administrator = await BuildAdministratorDtoAsync(target, actorUserId, cancellationToken)
            };
        }

        var owner = await _userManager.FindByEmailAsync(normalizedEmail);
        if (owner is not null && owner.Id != target.Id)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.EmailAlreadyInUse,
                ipAddress,
                cancellationToken);
            return Failure(
                AdminAccountOperationStatus.EmailAlreadyInUse,
                "Este e-mail já está em uso.");
        }

        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
        var wasActive = target.EmailConfirmed;
        AdminInvitation? issuedInvitation = null;

        try
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

            // A link issued for the previous address must never remain valid after the change.
            var activeInvitations = await LoadActiveInvitationsTrackedAsync(
                target.Id,
                cancellationToken);
            foreach (var invitation in activeInvitations)
            {
                invitation.SupersededAtUtc = utcNow;
                invitation.ClaimedAtUtc = null;
                invitation.ClaimedBy = null;
                invitation.MarkAsUpdated();
            }

            target.Email = normalizedEmail;
            target.UserName = normalizedEmail;
            target.EmailConfirmed = wasActive;

            var updateResult = await _userManager.UpdateAsync(target);
            if (!updateResult.Succeeded)
            {
                await RollBackAsync(transaction, cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var duplicate = updateResult.Errors.Any(
                    error => error.Code is "DuplicateEmail" or "DuplicateUserName");
                if (duplicate)
                {
                    await WriteDeniedAuditAsync(
                        actorUserId,
                        target.Id,
                        AdminAccountErrorCodes.EmailAlreadyInUse,
                        ipAddress,
                        cancellationToken);
                }

                return Failure(
                    duplicate
                        ? AdminAccountOperationStatus.EmailAlreadyInUse
                        : AdminAccountOperationStatus.Failed,
                    "Não foi possível alterar o e-mail.");
            }

            if (!wasActive)
            {
                issuedInvitation = await _invitationService.CreateAsync(
                    target.Id,
                    normalizedEmail,
                    AdminInvitationPurpose.AccountInvitation,
                    actorUserId,
                    cancellationToken);
            }

            if (wasActive)
            {
                await _refreshTokenRepository.RevokeAllForUserAsync(
                    target.Id,
                    utcNow,
                    cancellationToken);
                await _userManager.UpdateSecurityStampAsync(target);
            }

            AddAudit(
                actorUserId,
                target.Id,
                "AdminEmailChanged",
                $"Administrative e-mail changed from {currentEmail} to {normalizedEmail}.",
                ipAddress);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            if (issuedInvitation is not null)
            {
                EnqueueInvitationEmail(issuedInvitation.Id);
            }
            else if (wasActive)
            {
                EnqueueEmailChangedNotice(currentEmail, normalizedEmail);
            }

            _logger.LogInformation(
                "{EventType} | Administrator e-mail changed | UserId={UserId} | IP={IpAddress}",
                "ADMIN_EMAIL_CHANGED",
                target.Id,
                ipAddress);

            return new AdminAccountOperationResult
            {
                Status = AdminAccountOperationStatus.Succeeded,
                Administrator = await BuildAdministratorDtoAsync(target, actorUserId, cancellationToken),
                Invitation = issuedInvitation is null
                    ? null
                    : _invitationService.ToDto(
                        issuedInvitation,
                        _timeProvider.GetUtcNow().UtcDateTime)
            };
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();

            _logger.LogError(
                exception,
                "{EventType} | Administrator e-mail change failed | UserId={UserId} | IP={IpAddress}",
                "ADMIN_EMAIL_CHANGE_FAILED",
                target.Id,
                ipAddress);
            return Failure(AdminAccountOperationStatus.Failed, "Não foi possível alterar o e-mail.");
        }
    }

    public async Task<AdminAccountInvitationDeliveryResult> ResendInvitationAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var target = await LoadAdministratorAsync(targetUserId, cancellationToken);
        if (target is null)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                targetUserId,
                AdminAccountErrorCodes.AdminNotFound,
                ipAddress,
                cancellationToken);
            return DeliveryFailure(
                AdminAccountOperationStatus.NotFound,
                "Administrador não encontrado.");
        }

        if (target.EmailConfirmed)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.InvitationNotPending,
                ipAddress,
                cancellationToken);
            return DeliveryFailure(
                AdminAccountOperationStatus.InvitationNotPending,
                "Este administrador já ativou a conta.");
        }

        var activeInvitations = (await LoadActiveInvitationsTrackedAsync(target.Id, cancellationToken))
            .Where(invitation => invitation.Purpose == AdminInvitationPurpose.AccountInvitation)
            .ToList();

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var reusable = activeInvitations
            .Where(invitation => invitation.ExpiresAtUtc > utcNow)
            .OrderByDescending(invitation => invitation.CreatedAtUtc)
            .FirstOrDefault();

        AdminInvitation? invitation = reusable;
        var reissued = false;

        if (invitation is null && activeInvitations.Count == 0)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.InvitationNotPending,
                ipAddress,
                cancellationToken);
            return DeliveryFailure(
                AdminAccountOperationStatus.InvitationNotPending,
                "Não há convite pendente para este administrador.");
        }

        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            if (invitation is null)
            {
                foreach (var expired in activeInvitations)
                {
                    expired.SupersededAtUtc = utcNow;
                    expired.ClaimedAtUtc = null;
                    expired.ClaimedBy = null;
                    expired.MarkAsUpdated();
                }

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);

                invitation = await _invitationService.CreateAsync(
                    target.Id,
                    target.Email ?? string.Empty,
                    AdminInvitationPurpose.AccountInvitation,
                    actorUserId,
                    cancellationToken);
                reissued = true;
            }
            else if (invitation.EmailSentAtUtc is not null)
            {
                // A manual resend must produce a new delivery attempt (and therefore a distinct
                // Message-Id) while keeping the same link. Clearing the delivery marker lets the job
                // claim the invitation again; the token is derived from the same row, so the link the
                // administrator already received stays valid.
                invitation.EmailSentAtUtc = null;
                invitation.EmailSendingAtUtc = null;
                invitation.EmailSendAttemptId = null;
                invitation.MarkAsUpdated();
            }

            AddAudit(
                actorUserId,
                target.Id,
                "AdminInvitationResent",
                reissued
                    ? "Expired administrative invitation was superseded and a new link was issued."
                    : "Administrative invitation delivery was requested again without invalidating the existing link.",
                ipAddress);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();
            _logger.LogError(
                exception,
                "{EventType} | Administrative invitation resend failed | UserId={UserId} | IP={IpAddress}",
                "ADMIN_INVITATION_RESEND_FAILED",
                target.Id,
                ipAddress);
            return DeliveryFailure(
                AdminAccountOperationStatus.Failed,
                "Não foi possível reenviar o convite.");
        }

        EnqueueInvitationEmail(invitation!.Id);

        _logger.LogInformation(
            "{EventType} | Administrative invitation resent | UserId={UserId} | InvitationId={InvitationId} | Reissued={Reissued} | IP={IpAddress}",
            "ADMIN_INVITATION_RESENT",
            target.Id,
            invitation.Id,
            reissued,
            ipAddress);

        return new AdminAccountInvitationDeliveryResult
        {
            Status = AdminAccountOperationStatus.Succeeded,
            InvitationId = invitation.Id,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            EmailDeliveryPending = true
        };
    }

    public async Task<AdminAccountOperationResult> CancelInvitationAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var target = await LoadAdministratorAsync(targetUserId, cancellationToken);
        if (target is null)
        {
            // The identity of a cancelled invitation is deleted; the audit tombstone keeps the
            // documented 409 (INVITATION_NOT_PENDING) for a repeated cancellation instead of a
            // misleading 404.
            if (await WasInvitationCancelledAsync(targetUserId, cancellationToken))
            {
                await WriteDeniedAuditAsync(
                    actorUserId,
                    targetUserId,
                    AdminAccountErrorCodes.InvitationNotPending,
                    ipAddress,
                    cancellationToken);
                return Failure(
                    AdminAccountOperationStatus.InvitationNotPending,
                    "Este convite já foi cancelado.");
            }

            await WriteDeniedAuditAsync(
                actorUserId,
                targetUserId,
                AdminAccountErrorCodes.AdminNotFound,
                ipAddress,
                cancellationToken);
            return Failure(AdminAccountOperationStatus.NotFound, "Administrador não encontrado.");
        }

        if (target.EmailConfirmed)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.AdminNotActive,
                ipAddress,
                cancellationToken);
            return Failure(
                AdminAccountOperationStatus.NotActive,
                "Esta conta já foi ativada. Use bloqueio ou remoção.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var activeInvitations = await LoadActiveInvitationsTrackedAsync(target.Id, cancellationToken);
        if (activeInvitations.Count == 0)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.InvitationNotPending,
                ipAddress,
                cancellationToken);
            return Failure(
                AdminAccountOperationStatus.InvitationNotPending,
                "Não há convite pendente para este administrador.");
        }

        var pendingOnly = target.PasswordHash is null && !target.EmailConfirmed;
        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            foreach (var invitation in activeInvitations)
            {
                invitation.RevokedAtUtc = utcNow;
                invitation.ClaimedAtUtc = null;
                invitation.ClaimedBy = null;
                invitation.MarkAsUpdated();

                if (pendingOnly)
                {
                    invitation.UserId = null;
                }
            }

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);

            if (pendingOnly)
            {
                var deleteResult = await _userManager.DeleteAsync(target);
                if (!deleteResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Could not delete the never-activated administrative identity.");
                }
            }

            AddAudit(
                actorUserId,
                target.Id,
                "AdminInvitationCancelled",
                pendingOnly
                    ? "Administrative invitation cancelled; the never-activated identity was deleted."
                    : "Administrative invitation cancelled.",
                ipAddress);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "{EventType} | Administrative invitation cancelled | UserId={UserId} | IdentityDeleted={IdentityDeleted} | IP={IpAddress}",
                "ADMIN_INVITATION_CANCELLED",
                target.Id,
                pendingOnly,
                ipAddress);

            return new AdminAccountOperationResult { Status = AdminAccountOperationStatus.Succeeded };
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();
            _logger.LogError(
                exception,
                "{EventType} | Administrative invitation cancellation failed | UserId={UserId} | IP={IpAddress}",
                "ADMIN_INVITATION_CANCEL_FAILED",
                target.Id,
                ipAddress);
            return Failure(
                AdminAccountOperationStatus.Failed,
                "Não foi possível cancelar o convite.");
        }
    }

    public async Task<AdminAccountInvitationDeliveryResult> RequestPasswordResetAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var target = await LoadAdministratorAsync(targetUserId, cancellationToken);
        if (target is null)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                targetUserId,
                AdminAccountErrorCodes.AdminNotFound,
                ipAddress,
                cancellationToken);
            return DeliveryFailure(
                AdminAccountOperationStatus.NotFound,
                "Administrador não encontrado.");
        }

        if (!target.EmailConfirmed)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.AdminNotActive,
                ipAddress,
                cancellationToken);
            return DeliveryFailure(
                AdminAccountOperationStatus.NotActive,
                "Esta conta ainda não foi ativada. Reenvie o convite.");
        }

        var activeInvitations = (await LoadActiveInvitationsTrackedAsync(target.Id, cancellationToken))
            .Where(invitation => invitation.Purpose == AdminInvitationPurpose.PasswordReset)
            .ToList();

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var reusable = activeInvitations
            .Where(invitation => invitation.ExpiresAtUtc > utcNow)
            .OrderByDescending(invitation => invitation.CreatedAtUtc)
            .FirstOrDefault();

        AdminInvitation? invitation = reusable;
        var enqueueDelivery = reusable is null || reusable.EmailSentAtUtc is null;
        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            if (invitation is null)
            {
                foreach (var expired in activeInvitations)
                {
                    expired.SupersededAtUtc = utcNow;
                    expired.ClaimedAtUtc = null;
                    expired.ClaimedBy = null;
                    expired.MarkAsUpdated();
                }

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);

                invitation = await _invitationService.CreateAsync(
                    target.Id,
                    target.Email ?? string.Empty,
                    AdminInvitationPurpose.PasswordReset,
                    actorUserId,
                    cancellationToken);
            }

            // A possibly compromised session must stop renewing before the owner clicks the link.
            await _refreshTokenRepository.RevokeAllForUserAsync(target.Id, utcNow, cancellationToken);
            await _userManager.UpdateSecurityStampAsync(target);

            AddAudit(
                actorUserId,
                target.Id,
                "AdminPasswordResetRequested",
                "Administrative password reset requested; all sessions were revoked.",
                ipAddress);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();
            _logger.LogError(
                exception,
                "{EventType} | Administrative password reset request failed | UserId={UserId} | IP={IpAddress}",
                "ADMIN_PASSWORD_RESET_REQUEST_FAILED",
                target.Id,
                ipAddress);
            return DeliveryFailure(
                AdminAccountOperationStatus.Failed,
                "Não foi possível solicitar a redefinição de senha.");
        }

        var resolvedInvitation = invitation!;
        if (enqueueDelivery)
        {
            EnqueueInvitationEmail(resolvedInvitation.Id);
        }

        _logger.LogInformation(
            "{EventType} | Administrative password reset requested | UserId={UserId} | InvitationId={InvitationId} | IP={IpAddress}",
            "ADMIN_PASSWORD_RESET_REQUESTED",
            target.Id,
            resolvedInvitation.Id,
            ipAddress);

        return new AdminAccountInvitationDeliveryResult
        {
            Status = AdminAccountOperationStatus.Succeeded,
            InvitationId = resolvedInvitation.Id,
            ExpiresAtUtc = resolvedInvitation.ExpiresAtUtc,
            EmailDeliveryPending = enqueueDelivery
        };
    }

    public Task<AdminAccountOperationResult> BlockAsync(
        Guid targetUserId,
        string? reason,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
        => MutateWithLastActiveGuardAsync(
            targetUserId,
            actorUserId,
            ipAddress,
            requireActiveForGuard: true,
            async (target, transaction) =>
            {
                if (!target.EmailConfirmed)
                {
                    return Failure(
                        AdminAccountOperationStatus.NotActive,
                        "Este administrador ainda não ativou a conta. Reenvie o convite.");
                }

                var state = await _dbContext.AdminAccountStates
                    .SingleOrDefaultAsync(x => x.UserId == target.Id, cancellationToken);

                if (state is { IsBlocked: true })
                {
                    return Failure(
                        AdminAccountOperationStatus.AlreadyBlocked,
                        "Este administrador já está bloqueado.");
                }

                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
                if (state is null)
                {
                    state = new AdminAccountState { UserId = target.Id };
                    _dbContext.AdminAccountStates.Add(state);
                }

                state.IsBlocked = true;
                state.BlockedAtUtc = utcNow;
                state.BlockedByAdminId = actorUserId;
                state.MarkAsUpdated();

                await _refreshTokenRepository.RevokeAllForUserAsync(
                    target.Id,
                    utcNow,
                    cancellationToken);
                await _userManager.UpdateSecurityStampAsync(target);

                AddAudit(
                    actorUserId,
                    target.Id,
                    "AdminBlocked",
                    $"Administrator blocked. Reason: {SanitizeReason(reason) ?? "not informed"}. All sessions were revoked.",
                    ipAddress);

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);

                _logger.LogInformation(
                    "{EventType} | Administrator blocked | UserId={UserId} | IP={IpAddress}",
                    "ADMIN_BLOCKED",
                    target.Id,
                    ipAddress);

                return new AdminAccountOperationResult
                {
                    Status = AdminAccountOperationStatus.Succeeded,
                    Administrator = await BuildAdministratorDtoAsync(
                        target,
                        actorUserId,
                        cancellationToken)
                };
            },
            cancellationToken);

    public Task<AdminAccountOperationResult> UnblockAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
        => MutateWithLastActiveGuardAsync(
            targetUserId,
            actorUserId,
            ipAddress,
            requireActiveForGuard: false,
            async (target, transaction) =>
            {
                var state = await _dbContext.AdminAccountStates
                    .SingleOrDefaultAsync(x => x.UserId == target.Id, cancellationToken);

                if (state is null || !state.IsBlocked)
                {
                    return Failure(
                        AdminAccountOperationStatus.NotBlocked,
                        "Este administrador não está bloqueado.");
                }

                state.IsBlocked = false;
                state.BlockedAtUtc = null;
                state.BlockedByAdminId = null;
                state.MarkAsUpdated();

                // Administrative unblock is the support action for an administrator locked out by
                // the automatic Identity policy, so both mechanisms are cleared together.
                await _userManager.SetLockoutEndDateAsync(target, null);
                await _userManager.ResetAccessFailedCountAsync(target);

                AddAudit(
                    actorUserId,
                    target.Id,
                    "AdminUnblocked",
                    "Administrator unblocked; previous sessions were not restored.",
                    ipAddress);

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);

                _logger.LogInformation(
                    "{EventType} | Administrator unblocked | UserId={UserId} | IP={IpAddress}",
                    "ADMIN_UNBLOCKED",
                    target.Id,
                    ipAddress);

                return new AdminAccountOperationResult
                {
                    Status = AdminAccountOperationStatus.Succeeded,
                    Administrator = await BuildAdministratorDtoAsync(
                        target,
                        actorUserId,
                        cancellationToken)
                };
            },
            cancellationToken);

    public Task<AdminAccountOperationResult> RemoveAsync(
        Guid targetUserId,
        string? reason,
        Guid actorUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
        => MutateWithLastActiveGuardAsync(
            targetUserId,
            actorUserId,
            ipAddress,
            requireActiveForGuard: true,
            async (target, transaction) =>
            {
                var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
                var sanitizedReason = SanitizeReason(reason);

                if (!target.EmailConfirmed || target.PasswordHash is null)
                {
                    var invitations = await LoadActiveInvitationsTrackedAsync(
                        target.Id,
                        cancellationToken);
                    foreach (var invitation in invitations)
                    {
                        invitation.RevokedAtUtc = utcNow;
                        invitation.UserId = null;
                        invitation.ClaimedAtUtc = null;
                        invitation.ClaimedBy = null;
                        invitation.MarkAsUpdated();
                    }

                    await _efUnitOfWork.SaveChangesAsync(cancellationToken);
                    var deleteResult = await _userManager.DeleteAsync(target);
                    if (!deleteResult.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Could not delete the pending administrative identity.");
                    }

                    AddAudit(
                        actorUserId,
                        target.Id,
                        "AdminRemoved",
                        $"Pending administrator invitation removed ({AdminRemovalMode.PendingInvitationDeleted}). Reason: {sanitizedReason ?? "not informed"}.",
                        ipAddress);

                    await _efUnitOfWork.SaveChangesAsync(cancellationToken);
                    await CommitAsync(transaction, cancellationToken);

                    _logger.LogInformation(
                        "{EventType} | Pending administrator removed | UserId={UserId} | Mode={Mode} | IP={IpAddress}",
                        "ADMIN_REMOVED",
                        target.Id,
                        AdminRemovalMode.PendingInvitationDeleted,
                        ipAddress);

                    return new AdminAccountOperationResult { Status = AdminAccountOperationStatus.Succeeded };
                }

                // Revoke and rotate before dropping the role so a still-valid access token cannot
                // renew nor keep operating as an administrator after this response. Any outstanding
                // invitation/reset link is revoked too: the identity no longer manages the platform.
                var outstandingInvitations = await LoadActiveInvitationsTrackedAsync(
                    target.Id,
                    cancellationToken);
                foreach (var outstanding in outstandingInvitations)
                {
                    outstanding.RevokedAtUtc = utcNow;
                    outstanding.ClaimedAtUtc = null;
                    outstanding.ClaimedBy = null;
                    outstanding.MarkAsUpdated();
                }

                await _refreshTokenRepository.RevokeAllForUserAsync(
                    target.Id,
                    utcNow,
                    cancellationToken);
                await _userManager.UpdateSecurityStampAsync(target);

                var roleResult = await _userManager.RemoveFromRoleAsync(target, AdminRole);
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException("Could not remove the Admin role.");
                }

                AddAudit(
                    actorUserId,
                    target.Id,
                    "AdminRemoved",
                    $"Administrator role removed ({AdminRemovalMode.RoleRemoved}); identity and other roles preserved. Reason: {sanitizedReason ?? "not informed"}.",
                    ipAddress);

                await _efUnitOfWork.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);

                _logger.LogInformation(
                    "{EventType} | Administrator removed | UserId={UserId} | Mode={Mode} | IP={IpAddress}",
                    "ADMIN_REMOVED",
                    target.Id,
                    AdminRemovalMode.RoleRemoved,
                    ipAddress);

                return new AdminAccountOperationResult { Status = AdminAccountOperationStatus.Succeeded };
            },
            cancellationToken);

    public async Task<AdminInvitationValidationDto> ValidateInvitationTokenAsync(
        string token,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var matches = await _invitationService.FindByHashAsync(token, purpose, cancellationToken);
        var invitation = matches.FirstOrDefault();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (invitation is null)
        {
            return InvalidValidation();
        }

        var state = _invitationService.ResolveState(invitation, utcNow);
        if (state is not (AdminInvitationState.Pending or AdminInvitationState.Sending))
        {
            return InvalidValidation();
        }

        // A link issued before the role was removed must not present itself as usable.
        if (invitation.UserId is { } targetId)
        {
            var target = await _userManager.FindByIdAsync(targetId.ToString());
            if (target is null || !await _userManager.IsInRoleAsync(target, AdminRole))
            {
                return InvalidValidation();
            }
        }

        return new AdminInvitationValidationDto
        {
            Valid = true,
            Purpose = purpose.ToString(),
            EmailMasked = MaskEmail(invitation.Email),
            ExpiresAtUtc = invitation.ExpiresAtUtc
        };
    }

    public Task<AdminAccountSetupResultDto> AcceptInvitationAsync(
        string token,
        string newPassword,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
        => ConsumeInvitationAsync(
            token,
            newPassword,
            AdminInvitationPurpose.AccountInvitation,
            ipAddress,
            cancellationToken);

    public Task<AdminAccountSetupResultDto> ConfirmPasswordResetAsync(
        string token,
        string newPassword,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
        => ConsumeInvitationAsync(
            token,
            newPassword,
            AdminInvitationPurpose.PasswordReset,
            ipAddress,
            cancellationToken);

    private async Task<AdminAccountSetupResultDto> ConsumeInvitationAsync(
        string token,
        string newPassword,
        AdminInvitationPurpose purpose,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var claim = await _invitationService.TryClaimAsync(token, purpose, cancellationToken);
        if (!claim.IsValid || claim.Invitation is null || claim.ClaimAttemptId is null)
        {
            return claim.Status == AdminInvitationClaimStatus.AlreadyConsumed
                ? SetupFailure(
                    AdminAccountOperationStatus.TokenAlreadyUsed,
                    "Este link já foi usado.")
                : SetupFailure(
                    AdminAccountOperationStatus.InvalidOrExpiredToken,
                    "Este link é inválido ou expirou. Peça um novo convite a um administrador.");
        }

        var invitation = claim.Invitation;
        var claimAttemptId = claim.ClaimAttemptId.Value;
        var target = invitation.UserId is { } userId
            ? await _userManager.FindByIdAsync(userId.ToString())
            : null;

        if (target is null)
        {
            await _invitationService.ReleaseClaimAsync(invitation.Id, claimAttemptId, cancellationToken);
            return SetupFailure(
                AdminAccountOperationStatus.InvalidOrExpiredToken,
                "Este link é inválido ou expirou. Peça um novo convite a um administrador.");
        }

        // A link issued before the role was removed must never reactivate the identity.
        if (!await _userManager.IsInRoleAsync(target, AdminRole))
        {
            await _invitationService.ReleaseClaimAsync(invitation.Id, claimAttemptId, cancellationToken);
            return SetupFailure(
                AdminAccountOperationStatus.InvalidOrExpiredToken,
                "Este link é inválido ou expirou. Peça um novo convite a um administrador.");
        }

        if (purpose == AdminInvitationPurpose.PasswordReset && !target.EmailConfirmed)
        {
            await _invitationService.ReleaseClaimAsync(invitation.Id, claimAttemptId, cancellationToken);
            return SetupFailure(
                AdminAccountOperationStatus.InvalidOrExpiredToken,
                "Este link é inválido ou expirou. Peça um novo convite a um administrador.");
        }

        var passwordValidation = await ValidatePasswordAsync(target, newPassword);
        if (!passwordValidation.Succeeded)
        {
            await _invitationService.ReleaseClaimAsync(invitation.Id, claimAttemptId, cancellationToken);
            return SetupFailure(
                AdminAccountOperationStatus.WeakPassword,
                "A nova senha não atende aos requisitos de segurança.");
        }

        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(target);
            var resetResult = await _userManager.ResetPasswordAsync(target, resetToken, newPassword);
            if (!resetResult.Succeeded)
            {
                await RollBackAsync(transaction, cancellationToken);
                await _invitationService.ReleaseClaimAsync(
                    invitation.Id,
                    claimAttemptId,
                    cancellationToken);
                return SetupFailure(
                    AdminAccountOperationStatus.WeakPassword,
                    "A nova senha não atende aos requisitos de segurança.");
            }

            target.EmailConfirmed = true;
            target.AccessFailedCount = 0;
            target.LockoutEnd = null;

            var updateResult = await _userManager.UpdateAsync(target);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException("Could not finalize the administrative account.");
            }

            await _userManager.ResetAccessFailedCountAsync(target);
            await _userManager.SetLockoutEndDateAsync(target, null);
            await _refreshTokenRepository.RevokeAllForUserAsync(
                target.Id,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            var consumed = await _invitationService.MarkConsumedAsync(
                invitation.Id,
                claimAttemptId,
                cancellationToken);
            if (!consumed)
            {
                await RollBackAsync(transaction, cancellationToken);
                await _invitationService.ReleaseClaimAsync(
                    invitation.Id,
                    claimAttemptId,
                    cancellationToken);
                var current = await _invitationService.FindByIdAsync(invitation.Id, cancellationToken);
                return current?.ConsumedAtUtc is not null
                    ? SetupFailure(
                        AdminAccountOperationStatus.TokenAlreadyUsed,
                        "Este link já foi usado.")
                    : SetupFailure(
                        AdminAccountOperationStatus.InvalidOrExpiredToken,
                        "Este link é inválido ou expirou. Peça um novo convite a um administrador.");
            }

            AddAudit(
                target.Id,
                target.Id,
                purpose == AdminInvitationPurpose.PasswordReset
                    ? "AdminPasswordResetCompleted"
                    : "AdminInvitationAccepted",
                purpose == AdminInvitationPurpose.PasswordReset
                    ? "Administrative password reset completed."
                    : "Administrative invitation accepted; the account was activated.",
                ipAddress);

            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "{EventType} | Administrative link consumed | UserId={UserId} | Purpose={Purpose}",
                purpose == AdminInvitationPurpose.PasswordReset
                    ? "ADMIN_PASSWORD_RESET_COMPLETED"
                    : "ADMIN_INVITATION_ACCEPTED",
                target.Id,
                purpose);

            return new AdminAccountSetupResultDto
            {
                Succeeded = true,
                Purpose = purpose.ToString(),
                Message = purpose == AdminInvitationPurpose.PasswordReset
                    ? "Senha atualizada. Entre com a nova senha."
                    : "Conta ativada. Entre com sua nova senha."
            };
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();
            await _invitationService.ReleaseClaimAsync(
                invitation.Id,
                claimAttemptId,
                cancellationToken);

            _logger.LogError(
                exception,
                "{EventType} | Administrative link consumption failed | InvitationId={InvitationId} | Purpose={Purpose}",
                "ADMIN_INVITATION_CONSUME_FAILED",
                invitation.Id,
                purpose);

            return SetupFailure(
                AdminAccountOperationStatus.Failed,
                "Não foi possível concluir. Tente novamente.");
        }
    }

    private async Task<AdminAccountOperationResult> MutateWithLastActiveGuardAsync(
        Guid targetUserId,
        Guid actorUserId,
        string? ipAddress,
        bool requireActiveForGuard,
        Func<IdentityUser<Guid>, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?, Task<AdminAccountOperationResult>> mutation,
        CancellationToken cancellationToken)
    {
        var target = await LoadAdministratorAsync(targetUserId, cancellationToken);
        if (target is null)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                targetUserId,
                AdminAccountErrorCodes.AdminNotFound,
                ipAddress,
                cancellationToken);
            return Failure(AdminAccountOperationStatus.NotFound, "Administrador não encontrado.");
        }

        if (target.Id == actorUserId)
        {
            await WriteDeniedAuditAsync(
                actorUserId,
                target.Id,
                AdminAccountErrorCodes.SelfActionNotAllowed,
                ipAddress,
                cancellationToken);
            return Failure(
                AdminAccountOperationStatus.SelfActionNotAllowed,
                "Você não pode executar esta ação na própria conta.");
        }

        var useInMemoryLock = !_dbContext.Database.IsRelational();
        if (useInMemoryLock)
        {
            await InMemoryLastActiveLock.WaitAsync(cancellationToken);
        }

        var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            if (_dbContext.Database.IsRelational())
            {
                await AcquireLastActiveLockAsync(cancellationToken);
            }

            if (requireActiveForGuard && await IsTargetCurrentlyActiveAsync(target, cancellationToken))
            {
                var otherActive = await CountActiveAdministratorsExcludingAsync(
                    target.Id,
                    cancellationToken);
                if (otherActive == 0)
                {
                    await RollBackAsync(transaction, cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                    await WriteDeniedAuditAsync(
                        actorUserId,
                        target.Id,
                        AdminAccountErrorCodes.LastActiveAdmin,
                        ipAddress,
                        cancellationToken);

                    return Failure(
                        AdminAccountOperationStatus.LastActiveAdmin,
                        "A plataforma precisa manter pelo menos um administrador ativo.");
                }
            }

            var result = await mutation(target, transaction);
            if (!result.Succeeded)
            {
                // A business-rule rejection (already blocked, not blocked, invitation not active)
                // must survive as an audit row even though the mutation's transaction is discarded.
                await RollBackAsync(transaction, cancellationToken);
                _dbContext.ChangeTracker.Clear();
                await WriteDeniedAuditAsync(
                    actorUserId,
                    target.Id,
                    result.Code ?? AdminAccountErrorCodes.AdminNotFound,
                    ipAddress,
                    cancellationToken);
            }

            return result;
        }
        catch (Exception exception)
        {
            await RollBackAsync(transaction, cancellationToken);
            _dbContext.ChangeTracker.Clear();

            _logger.LogError(
                exception,
                "{EventType} | Administrator mutation failed | ActorId={ActorId} | TargetId={TargetId} | IP={IpAddress}",
                "ADMIN_ACCOUNT_MUTATION_FAILED",
                actorUserId,
                targetUserId,
                ipAddress);

            return Failure(
                AdminAccountOperationStatus.Failed,
                "Não foi possível concluir a operação.");
        }
        finally
        {
            if (useInMemoryLock)
            {
                InMemoryLastActiveLock.Release();
            }
        }
    }

    private async Task<IdentityUser<Guid>?> LoadAdministratorAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        return await _userManager.IsInRoleAsync(user, AdminRole) ? user : null;
    }

    private async Task<AdminAdministratorDto> BuildAdministratorDtoAsync(
        IdentityUser<Guid> user,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var state = await _dbContext.AdminAccountStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);

        var invitation = await _dbContext.AdminInvitations
            .AsNoTracking()
            .Where(x => x.UserId == user.Id
                        && x.Purpose == AdminInvitationPurpose.AccountInvitation
                        && x.ConsumedAtUtc == null
                        && x.RevokedAtUtc == null
                        && x.SupersededAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var isBlocked = state?.IsBlocked ?? false;
        var isActive = user.EmailConfirmed && !isBlocked;
        var activeCount = await CountActiveAdministratorsExcludingAsync(Guid.Empty, cancellationToken);

        // AspNetUsers has no creation timestamp; the earliest invitation is the best available
        // approximation and seeded administrators fall back to the Unix epoch.
        var createdAtUtc = await _dbContext.AdminInvitations
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => (DateTime?)x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        return new AdminAdministratorDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Status = !user.EmailConfirmed ? "Invited" : isBlocked ? "Blocked" : "Active",
            IsEmailConfirmed = user.EmailConfirmed,
            IsBlocked = isBlocked,
            IsCurrentUser = user.Id == currentUserId,
            IsLastActiveAdmin = isActive && activeCount == 1,
            CreatedAtUtc = createdAtUtc ?? DateTime.UnixEpoch,
            Invitation = invitation is null
                ? null
                : _invitationService.ToDto(invitation, utcNow)
        };
    }

    private async Task<List<AdminInvitation>> LoadActiveInvitationsTrackedAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => await _dbContext.AdminInvitations
            .Where(x => x.UserId == userId
                        && x.ConsumedAtUtc == null
                        && x.RevokedAtUtc == null
                        && x.SupersededAtUtc == null)
            .ToListAsync(cancellationToken);

    private async Task<bool> IsTargetCurrentlyActiveAsync(
        IdentityUser<Guid> target,
        CancellationToken cancellationToken)
    {
        if (!target.EmailConfirmed)
        {
            return false;
        }

        return !await _dbContext.AdminAccountStates
            .AnyAsync(x => x.UserId == target.Id && x.IsBlocked, cancellationToken);
    }

    private async Task<int> CountActiveAdministratorsExcludingAsync(
        Guid excludedUserId,
        CancellationToken cancellationToken)
        => await (from userRole in _dbContext.UserRoles.AsNoTracking()
                  join user in _dbContext.Users.AsNoTracking() on userRole.UserId equals user.Id
                  join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                  where role.Name == AdminRole
                        && user.Id != excludedUserId
                        && user.EmailConfirmed
                        && !_dbContext.AdminAccountStates.Any(
                            state => state.UserId == user.Id && state.IsBlocked)
                  select user.Id).CountAsync(cancellationToken);

    private async Task AcquireLastActiveLockAsync(CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))",
            ["admin-accounts:last-active"],
            cancellationToken);
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionIfRelationalAsync(
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    private static async Task CommitAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private static async Task RollBackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // A broken connection leaves PostgreSQL to roll back on dispose. Never mask the
            // original failure.
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private async Task<IdentityResult> ValidatePasswordAsync(
        IdentityUser<Guid> user,
        string password)
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

    private void AddAudit(
        Guid? userId,
        Guid? entityId,
        string auditEvent,
        string description,
        string? ipAddress)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Event = auditEvent,
            Entity = nameof(IdentityUser<Guid>),
            EntityId = entityId,
            Description = description,
            IpAddress = ipAddress
        });
    }

    private async Task WriteDeniedAuditAsync(
        Guid? actorUserId,
        Guid? targetUserId,
        string code,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "{EventType} | Administrative action denied | ActorId={ActorId} | TargetId={TargetId} | Code={Code} | IP={IpAddress}",
            "ADMIN_ACTION_DENIED",
            actorUserId,
            targetUserId,
            code,
            ipAddress);

        try
        {
            AddAudit(
                actorUserId,
                targetUserId,
                "AdminActionDenied",
                $"Administrative action denied: {code}.",
                ipAddress);
            await _efUnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "{EventType} | Administrative denial audit failed | Code={Code}",
                "ADMIN_ACTION_DENIED_AUDIT_FAILED",
                code);
        }
    }

    private async Task TryDeleteInMemoryUserAsync(IdentityUser<Guid> user)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();
            var tracked = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == user.Id);
            if (tracked is null)
            {
                return;
            }

            _dbContext.Users.Remove(tracked);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "{EventType} | Could not clean up an invited identity after a failed invitation | UserId={UserId}",
                "ADMIN_INVITATION_CLEANUP_FAILED",
                user.Id);
        }
    }

    private static AdminInvitationValidationDto InvalidValidation() => new()
    {
        Valid = false,
        Message = "Este link é inválido ou expirou. Peça um novo convite a um administrador."
    };

    private static AdminAccountOperationResult Failure(
        AdminAccountOperationStatus status,
        string error) => new()
    {
        Status = status,
        Error = error,
        Code = AdminAccountErrorCodes.ForStatus(status)
    };

    private static AdminAccountInvitationDeliveryResult DeliveryFailure(
        AdminAccountOperationStatus status,
        string error) => new()
    {
        Status = status,
        Error = error,
        Code = AdminAccountErrorCodes.ForStatus(status)
    };

    private static AdminAccountSetupResultDto SetupFailure(
        AdminAccountOperationStatus status,
        string error) => new()
    {
        Succeeded = false,
        Error = error,
        Code = AdminAccountErrorCodes.ForStatus(status)
    };

    private Task<bool> WasInvitationCancelledAsync(
        Guid targetUserId,
        CancellationToken cancellationToken)
        => _dbContext.AuditLogs
            .AsNoTracking()
            .AnyAsync(
                audit => audit.Entity == nameof(IdentityUser<Guid>)
                         && audit.Event == "AdminInvitationCancelled"
                         && audit.EntityId == targetUserId,
                cancellationToken);

    private void EnqueueInvitationEmail(Guid invitationId)
    {
        try
        {
            _backgroundJobClient.Enqueue<SendAdminAccountEmailJob>(
                job => job.ExecuteAsync(invitationId));
        }
        catch (Exception exception)
        {
            // The mutation is already committed. Losing the enqueue must not turn a successful
            // invitation into a 500 nor hide it from the list: the row stays with
            // EmailSentAtUtc = null ("convite em envio") and a manual resend recovers it.
            _logger.LogWarning(
                exception,
                "{EventType} | Could not enqueue the administrative invitation e-mail | InvitationId={InvitationId}",
                "ADMIN_INVITATION_ENQUEUE_FAILED",
                invitationId);
        }
    }

    private void EnqueueEmailChangedNotice(string oldEmail, string newEmail)
    {
        try
        {
            _backgroundJobClient.Enqueue<SendAdminEmailChangedNoticeJob>(
                job => job.ExecuteAsync(oldEmail, newEmail));
        }
        catch (Exception exception)
        {
            // Same rationale: the address change is authoritative and already committed.
            _logger.LogWarning(
                exception,
                "{EventType} | Could not enqueue the administrative e-mail change notice",
                "ADMIN_EMAIL_CHANGE_NOTICE_ENQUEUE_FAILED");
        }
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email)
        => email.Length is > 3 and <= 254
           && email.Contains('@')
           && !email.Contains(' ');

    private static string? SanitizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var sanitized = new string(reason
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();

        return sanitized.Length <= 200 ? sanitized : sanitized[..200];
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return "***";
        }

        return $"{email[0]}***{email[atIndex..]}";
    }

    private static class AdminRemovalMode
    {
        public const string RoleRemoved = "RoleRemoved";
        public const string PendingInvitationDeleted = "PendingInvitationDeleted";
    }
}
