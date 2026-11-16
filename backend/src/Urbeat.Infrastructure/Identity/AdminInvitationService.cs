using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Derives and consumes administrative e-mail tokens. Only the token hash is persisted; the raw
/// value is rebuilt from the invitation id, the purpose and the dedicated signing key, so a lost
/// enqueue or a process restart never strands a pending link.
/// </summary>
public sealed class AdminInvitationService : IAdminInvitationService
{
    private const string InvitationPurposePrefix = "admin-invitation:v1:";
    private const string PasswordResetPurposePrefix = "admin-password-reset:v1:";
    private const int InvitationIdLength = 16;
    private const int MacLength = 32;
    private const int TokenLength = InvitationIdLength + MacLength;

    private readonly ApplicationDbContext _dbContext;
    private readonly ITokenHasher _tokenHasher;
    private readonly AdminAccountOptions _options;
    private readonly byte[] _signingKey;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AdminInvitationService> _logger;

    public AdminInvitationService(
        ApplicationDbContext dbContext,
        ITokenHasher tokenHasher,
        IOptions<AdminAccountOptions> options,
        IOptions<RolePromotionChallengeOptions> signingOptions,
        TimeProvider timeProvider,
        ILogger<AdminInvitationService> logger)
    {
        _dbContext = dbContext;
        _tokenHasher = tokenHasher;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        var configurationErrors = signingOptions.Value.Validate();
        if (configurationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Administrative invitation signing key is not configured correctly: "
                + string.Join(" ", configurationErrors));
        }

        _signingKey = Encoding.UTF8.GetBytes(signingOptions.Value.SigningKey);
    }

    public string CreateToken(Guid invitationId, AdminInvitationPurpose purpose)
    {
        Span<byte> buffer = stackalloc byte[TokenLength];
        invitationId.TryWriteBytes(buffer[..InvitationIdLength]);
        ComputeMac(purpose, buffer[..InvitationIdLength], buffer[InvitationIdLength..]);
        return Base64UrlEncode(buffer);
    }

    public async Task<AdminInvitation> CreateAsync(
        Guid? userId,
        string email,
        AdminInvitationPurpose purpose,
        Guid? createdByAdminId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var invitation = new AdminInvitation
        {
            UserId = userId,
            Email = NormalizeEmail(email),
            Purpose = purpose,
            ExpiresAtUtc = utcNow + Lifetime(purpose),
            CreatedByAdminId = createdByAdminId,
            ConcurrencyVersion = 0
        };

        // The token is derived from the id, so the hash can only be computed after BaseEntity has
        // assigned it.
        var rawToken = CreateToken(invitation.Id, purpose);
        invitation.TokenHash = _tokenHasher.Hash(rawToken);

        _dbContext.AdminInvitations.Add(invitation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "{EventType} | Administrative invitation created | InvitationId={InvitationId} | Purpose={Purpose}",
            "ADMIN_INVITATION_CREATED",
            invitation.Id,
            purpose);

        return invitation;
    }

    public Task<AdminInvitation?> FindByIdAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default)
        => _dbContext.AdminInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<AdminInvitation>> FindActiveAsync(
        Guid userId,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default)
        => await _dbContext.AdminInvitations
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && x.Purpose == purpose
                        && x.ConsumedAtUtc == null
                        && x.RevokedAtUtc == null
                        && x.SupersededAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AdminInvitation>> FindByHashAsync(
        string token,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadInvitationId(token, purpose, out var invitationId))
        {
            return [];
        }

        var invitation = await _dbContext.AdminInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == invitationId && x.Purpose == purpose,
                cancellationToken);

        if (invitation is null || !_tokenHasher.Verify(token.Trim(), invitation.TokenHash))
        {
            return [];
        }

        return [invitation];
    }

    public async Task<AdminInvitationClaimResult> TryClaimAsync(
        string token,
        AdminInvitationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var matches = await FindByHashAsync(token, purpose, cancellationToken);
        var snapshot = matches.FirstOrDefault();
        if (snapshot is null)
        {
            return new AdminInvitationClaimResult { Status = AdminInvitationClaimStatus.Invalid };
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var state = ResolveState(snapshot, utcNow);
        if (state is not (AdminInvitationState.Pending or AdminInvitationState.Sending))
        {
            return new AdminInvitationClaimResult
            {
                Status = ClassifyState(state),
                Invitation = snapshot
            };
        }

        var claimAttemptId = Guid.CreateVersion7();
        var leaseThreshold = utcNow - _options.ClaimLease;

        if (_dbContext.Database.IsRelational())
        {
            var claimed = await _dbContext.AdminInvitations
                .Where(x => x.Id == snapshot.Id
                            && x.ConsumedAtUtc == null
                            && x.RevokedAtUtc == null
                            && x.SupersededAtUtc == null
                            && x.ExpiresAtUtc > utcNow
                            && (x.ClaimedAtUtc == null || x.ClaimedAtUtc < leaseThreshold))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.ClaimedAtUtc, utcNow)
                        .SetProperty(x => x.ClaimedBy, claimAttemptId)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1)
                        .SetProperty(x => x.UpdatedAtUtc, utcNow),
                    cancellationToken);

            if (claimed == 0)
            {
                var current = await FindByIdAsync(snapshot.Id, cancellationToken);
                return new AdminInvitationClaimResult
                {
                    Status = current is null
                        ? AdminInvitationClaimStatus.Invalid
                        : ClassifyUnclaimable(current, utcNow),
                    Invitation = current ?? snapshot
                };
            }
        }
        else
        {
            var tracked = await _dbContext.AdminInvitations
                .SingleOrDefaultAsync(x => x.Id == snapshot.Id, cancellationToken);

            if (tracked is null
                || tracked.ConsumedAtUtc is not null
                || tracked.RevokedAtUtc is not null
                || tracked.SupersededAtUtc is not null
                || tracked.ExpiresAtUtc <= utcNow
                || (tracked.ClaimedAtUtc is { } claimedAt && claimedAt >= leaseThreshold))
            {
                return new AdminInvitationClaimResult
                {
                    Status = tracked is null
                        ? AdminInvitationClaimStatus.Invalid
                        : ClassifyUnclaimable(tracked, utcNow),
                    Invitation = tracked ?? snapshot
                };
            }

            tracked.ClaimedAtUtc = utcNow;
            tracked.ClaimedBy = claimAttemptId;
            tracked.ConcurrencyVersion++;
            tracked.MarkAsUpdated();

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                var current = await FindByIdAsync(snapshot.Id, cancellationToken);
                return new AdminInvitationClaimResult
                {
                    Status = current is null
                        ? AdminInvitationClaimStatus.Invalid
                        : ClassifyUnclaimable(current, utcNow),
                    Invitation = current ?? snapshot
                };
            }
        }

        return new AdminInvitationClaimResult
        {
            Status = AdminInvitationClaimStatus.Valid,
            Invitation = snapshot,
            ClaimAttemptId = claimAttemptId,
            Token = token.Trim()
        };
    }

    public async Task<bool> MarkConsumedAsync(
        Guid invitationId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default)
    {
        if (claimAttemptId == Guid.Empty)
        {
            return false;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var leaseThreshold = utcNow - _options.ClaimLease;

        if (_dbContext.Database.IsRelational())
        {
            var updated = await _dbContext.AdminInvitations
                .Where(x => x.Id == invitationId
                            && x.ConsumedAtUtc == null
                            && x.RevokedAtUtc == null
                            && x.SupersededAtUtc == null
                            && x.ClaimedBy == claimAttemptId
                            && x.ClaimedAtUtc != null
                            && x.ClaimedAtUtc >= leaseThreshold)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.ConsumedAtUtc, utcNow)
                        .SetProperty(x => x.ClaimedAtUtc, (DateTime?)null)
                        .SetProperty(x => x.ClaimedBy, (Guid?)null)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1)
                        .SetProperty(x => x.UpdatedAtUtc, utcNow),
                    cancellationToken);

            return updated > 0;
        }

        var invitation = await _dbContext.AdminInvitations
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (invitation is null
            || invitation.ConsumedAtUtc is not null
            || invitation.RevokedAtUtc is not null
            || invitation.SupersededAtUtc is not null
            || invitation.ClaimedBy != claimAttemptId
            || invitation.ClaimedAtUtc is not { } claimedAt
            || claimedAt < leaseThreshold)
        {
            return false;
        }

        invitation.ConsumedAtUtc = utcNow;
        invitation.ClaimedAtUtc = null;
        invitation.ClaimedBy = null;
        invitation.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public async Task ReleaseClaimAsync(
        Guid invitationId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default)
    {
        if (claimAttemptId == Guid.Empty)
        {
            return;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.AdminInvitations
                .Where(x => x.Id == invitationId
                            && x.ConsumedAtUtc == null
                            && x.ClaimedBy == claimAttemptId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.ClaimedAtUtc, (DateTime?)null)
                        .SetProperty(x => x.ClaimedBy, (Guid?)null)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1)
                        .SetProperty(x => x.UpdatedAtUtc, utcNow),
                    cancellationToken);
            return;
        }

        var invitation = await _dbContext.AdminInvitations
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (invitation is null
            || invitation.ConsumedAtUtc is not null
            || invitation.ClaimedBy != claimAttemptId)
        {
            return;
        }

        invitation.ClaimedAtUtc = null;
        invitation.ClaimedBy = null;
        invitation.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt already took over the lease or consumed the invitation.
        }
    }

    public async Task<AdminInvitationEmailDelivery?> TryClaimEmailDeliveryAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var attemptId = Guid.CreateVersion7();
        var leaseThreshold = utcNow - _options.EmailSendLease;

        if (_dbContext.Database.IsRelational())
        {
            var snapshot = await FindByIdAsync(invitationId, cancellationToken);
            if (!IsEmailDeliverable(snapshot, utcNow))
            {
                return null;
            }

            var claimed = await _dbContext.AdminInvitations
                .Where(x => x.Id == invitationId
                            && x.EmailSentAtUtc == null
                            && x.ConsumedAtUtc == null
                            && x.RevokedAtUtc == null
                            && x.SupersededAtUtc == null
                            && x.ExpiresAtUtc > utcNow
                            && (x.EmailSendingAtUtc == null || x.EmailSendingAtUtc < leaseThreshold))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.EmailSendingAtUtc, utcNow)
                        .SetProperty(x => x.EmailSendAttemptId, attemptId)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1)
                        .SetProperty(x => x.UpdatedAtUtc, utcNow),
                    cancellationToken);

            return claimed == 0 ? null : BuildDelivery(snapshot!, attemptId);
        }

        var invitation = await _dbContext.AdminInvitations
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (!IsEmailDeliverable(invitation, utcNow)
            || (invitation!.EmailSendingAtUtc is { } sendingAt && sendingAt >= leaseThreshold))
        {
            return null;
        }

        invitation.EmailSendingAtUtc = utcNow;
        invitation.EmailSendAttemptId = attemptId;
        invitation.ConcurrencyVersion++;
        invitation.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        return BuildDelivery(invitation, attemptId);
    }

    public async Task MarkEmailSentAsync(
        Guid invitationId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.AdminInvitations
                .Where(x => x.Id == invitationId
                            && x.EmailSentAtUtc == null
                            && x.EmailSendAttemptId == attemptId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.EmailSentAtUtc, utcNow)
                        .SetProperty(x => x.EmailSendingAtUtc, (DateTime?)null)
                        .SetProperty(x => x.EmailSendAttemptId, (Guid?)null)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1)
                        .SetProperty(x => x.UpdatedAtUtc, utcNow),
                    cancellationToken);
            return;
        }

        var invitation = await _dbContext.AdminInvitations
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (invitation is null
            || invitation.EmailSentAtUtc is not null
            || invitation.EmailSendAttemptId != attemptId)
        {
            return;
        }

        invitation.EmailSentAtUtc = utcNow;
        invitation.EmailSendingAtUtc = null;
        invitation.EmailSendAttemptId = null;
        invitation.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent delivery already recorded the send; that is the desired final state.
        }
    }

    public async Task ReleaseEmailSendLeaseAsync(
        Guid invitationId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.AdminInvitations
                .Where(x => x.Id == invitationId
                            && x.EmailSentAtUtc == null
                            && x.EmailSendAttemptId == attemptId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.EmailSendingAtUtc, (DateTime?)null)
                        .SetProperty(x => x.EmailSendAttemptId, (Guid?)null)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1)
                        .SetProperty(x => x.UpdatedAtUtc, utcNow),
                    cancellationToken);
            return;
        }

        var invitation = await _dbContext.AdminInvitations
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (invitation is null
            || invitation.EmailSentAtUtc is not null
            || invitation.EmailSendAttemptId != attemptId)
        {
            return;
        }

        invitation.EmailSendingAtUtc = null;
        invitation.EmailSendAttemptId = null;
        invitation.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt already took over the lease.
        }
    }

    public AdminInvitationState ResolveState(AdminInvitation invitation, DateTime utcNow)
    {
        if (invitation.ConsumedAtUtc is not null)
        {
            return AdminInvitationState.Accepted;
        }

        if (invitation.RevokedAtUtc is not null)
        {
            return AdminInvitationState.Revoked;
        }

        if (invitation.SupersededAtUtc is not null)
        {
            return AdminInvitationState.Superseded;
        }

        if (invitation.ExpiresAtUtc <= utcNow)
        {
            return AdminInvitationState.Expired;
        }

        if (invitation.EmailSendingAtUtc is { } sendingAt
            && sendingAt >= utcNow - _options.EmailSendLease)
        {
            return AdminInvitationState.Sending;
        }

        return AdminInvitationState.Pending;
    }

    public AdminInvitationDto ToDto(AdminInvitation invitation, DateTime utcNow) => new()
    {
        Id = invitation.Id,
        Purpose = invitation.Purpose.ToString(),
        State = ResolveState(invitation, utcNow).ToString(),
        ExpiresAtUtc = invitation.ExpiresAtUtc,
        EmailSentAtUtc = invitation.EmailSentAtUtc,
        CreatedAtUtc = invitation.CreatedAtUtc
    };

    private AdminInvitationEmailDelivery BuildDelivery(AdminInvitation invitation, Guid attemptId) => new()
    {
        InvitationId = invitation.Id,
        AttemptId = attemptId,
        Email = invitation.Email,
        Purpose = invitation.Purpose,
        Token = CreateToken(invitation.Id, invitation.Purpose),
        ExpiresAtUtc = invitation.ExpiresAtUtc
    };

    private static bool IsEmailDeliverable(AdminInvitation? invitation, DateTime utcNow)
        => invitation is not null
           && invitation.EmailSentAtUtc is null
           && invitation.ConsumedAtUtc is null
           && invitation.RevokedAtUtc is null
           && invitation.SupersededAtUtc is null
           && invitation.ExpiresAtUtc > utcNow;

    private static AdminInvitationClaimStatus ClassifyState(AdminInvitationState state) => state switch
    {
        AdminInvitationState.Accepted => AdminInvitationClaimStatus.AlreadyConsumed,
        AdminInvitationState.Revoked => AdminInvitationClaimStatus.Revoked,
        AdminInvitationState.Superseded => AdminInvitationClaimStatus.Superseded,
        AdminInvitationState.Expired => AdminInvitationClaimStatus.Expired,
        _ => AdminInvitationClaimStatus.Valid
    };

    /// <summary>
    /// A live consumption lease means another attempt is applying the password right now, which is
    /// indistinguishable from "already used" for the caller.
    /// </summary>
    private AdminInvitationClaimStatus ClassifyUnclaimable(AdminInvitation invitation, DateTime utcNow)
    {
        var state = ResolveState(invitation, utcNow);
        if (state is AdminInvitationState.Pending or AdminInvitationState.Sending
            && invitation.ClaimedAtUtc is { } claimedAt
            && claimedAt >= utcNow - _options.ClaimLease)
        {
            return AdminInvitationClaimStatus.AlreadyConsumed;
        }

        return ClassifyState(state);
    }

    private TimeSpan Lifetime(AdminInvitationPurpose purpose) => purpose == AdminInvitationPurpose.PasswordReset
        ? _options.PasswordResetLifetime
        : _options.InvitationLifetime;

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private bool TryReadInvitationId(string token, AdminInvitationPurpose purpose, out Guid invitationId)
    {
        invitationId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Base64UrlDecode(token.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != TokenLength)
        {
            return false;
        }

        Span<byte> expectedMac = stackalloc byte[MacLength];
        ComputeMac(purpose, bytes.AsSpan(0, InvitationIdLength), expectedMac);

        if (!CryptographicOperations.FixedTimeEquals(
                expectedMac,
                bytes.AsSpan(InvitationIdLength, MacLength)))
        {
            return false;
        }

        invitationId = new Guid(bytes.AsSpan(0, InvitationIdLength));
        return true;
    }

    private void ComputeMac(
        AdminInvitationPurpose purpose,
        ReadOnlySpan<byte> invitationId,
        Span<byte> destination)
    {
        var purposePrefix = purpose == AdminInvitationPurpose.PasswordReset
            ? PasswordResetPurposePrefix
            : InvitationPurposePrefix;

        Span<byte> input = stackalloc byte[purposePrefix.Length + InvitationIdLength];
        Encoding.UTF8.GetBytes(purposePrefix, input);
        invitationId.CopyTo(input[purposePrefix.Length..]);

        using var hmac = new HMACSHA256(_signingKey);
        if (!hmac.TryComputeHash(input, destination, out _))
        {
            throw new InvalidOperationException("Could not derive the administrative invitation token.");
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            _ => normalized
        };

        return Convert.FromBase64String(normalized);
    }
}
