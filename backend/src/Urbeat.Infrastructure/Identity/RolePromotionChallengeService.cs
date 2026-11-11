using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.Infrastructure.Identity;

/// <summary>
/// Persists role promotion requests and consumes their opaque tokens exactly once. A dedicated
/// table is used instead of the signed e-mail change challenge so a promotion token can never be
/// replayed against e-mail confirmation or e-mail change endpoints.
/// </summary>
/// <remarks>
/// The confirmation token is derived with HMAC-SHA256 from the request id and the server signing
/// key and is never stored, so a pending request can always be re-sent: losing the original job
/// enqueue, a Redis flush or a worker restart cannot strand the link. Only the token hash is
/// persisted and it is still checked on every confirmation.
/// <para>
/// A request that is still active is reused rather than superseded, and a promotion delivered for
/// the same identity within the configured cooldown is not re-sent. Both rules keep a legitimate
/// link valid under repeated requests and bound how many messages an unauthenticated caller can
/// trigger for an address it only knows.
/// </para>
/// </remarks>
public sealed class RolePromotionChallengeService : IRolePromotionChallengeService
{
    /// <summary>Approved lifetime for the promotion link.</summary>
    public static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long a confirmation attempt may hold the challenge before another attempt (for example
    /// after a process crash) may claim it again. Long enough to cover the role/credential write,
    /// short enough that a crashed attempt never blocks the account owner.
    /// </summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Lease taken by the delivery job before handing the message to the provider. While it is
    /// valid, duplicate enqueues and the recovery sweep must not send again. It also bounds how
    /// long a crashed send blocks recovery.
    /// </summary>
    public static readonly TimeSpan EmailSendLease = TimeSpan.FromMinutes(5);

    // Domain separation for the derived token. Changing it invalidates every outstanding link.
    private const string TokenPurpose = "role-promotion:v1:";

    // Domain separation for the log-only identity key; never used to authorize anything.
    private const string IdentityKeyPurpose = "role-promotion-identity:v1:";
    private const int RequestIdLength = 16;
    private const int MacLength = 32;
    private const int TokenLength = RequestIdLength + MacLength;

    private readonly ApplicationDbContext _dbContext;
    private readonly byte[] _signingKey;
    private readonly TimeSpan _identityCooldown;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RolePromotionChallengeService> _logger;

    public RolePromotionChallengeService(
        ApplicationDbContext dbContext,
        IOptions<RolePromotionChallengeOptions> options,
        TimeProvider timeProvider,
        ILogger<RolePromotionChallengeService> logger)
    {
        _dbContext = dbContext;

        // Fail closed: no JWT/e-mail-change fallback and no default value. The same validation runs
        // at startup through ValidateOnStart; this check protects direct construction (tests, jobs
        // resolved early) and never includes the key in the message.
        var configurationErrors = options.Value.Validate();
        if (configurationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Role promotion signing key is not configured correctly: "
                + string.Join(" ", configurationErrors));
        }

        _signingKey = Encoding.UTF8.GetBytes(options.Value.SigningKey);
        _identityCooldown = options.Value.CreationCooldown;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RolePromotionChallengeCreated> CreateAsync(
        RolePromotionRequestData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(data.Email)
            || string.IsNullOrWhiteSpace(data.PasswordHash))
        {
            throw new ArgumentException("A role promotion request requires a user, e-mail and password hash.", nameof(data));
        }

        var normalizedEmail = data.Email.Trim().ToLowerInvariant();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        // Creation must be serialized per identity/role: two simultaneous registrations used to be
        // able to leave two active requests, and only one of them could ever be confirmed. An
        // advisory transaction lock (PostgreSQL) plus the filtered unique index on the pending row
        // guarantee a single winner.
        var ownsTransaction = false;
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null)
        {
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            ownsTransaction = true;
        }

        try
        {
            await AcquireCreationLockAsync(data.UserId, data.TargetRole, cancellationToken);

            var pendingRequests = await _dbContext.RolePromotionRequests
                .Where(x => x.UserId == data.UserId
                            && x.TargetRole == data.TargetRole
                            && x.ConsumedAtUtc == null
                            && x.SupersededAtUtc == null)
                .ToListAsync(cancellationToken);

            // Reuse instead of supersede. A repeated registration used to invalidate the link that
            // was already delivered before the new row was persisted; when the provider has no
            // transaction, a failure in that second write left the account without any usable link.
            // The existing request also keeps the password chosen by its owner: a repeat cannot be
            // used to change the pending credential until the link expires.
            var activeRequest = pendingRequests.FirstOrDefault(x => x.ExpiresAtUtc > utcNow);
            if (activeRequest is not null)
            {
                if (ownsTransaction)
                {
                    await _dbContext.Database.CommitTransactionAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "{EventType} | Reusing active role promotion request | RequestId={RequestId} | IdentityKey={IdentityKey} | Role={Role} | EmailSent={EmailSent}",
                    "ROLE_PROMOTION_REQUEST_REUSED",
                    activeRequest.Id,
                    ComputeIdentityKey(normalizedEmail),
                    activeRequest.TargetRole,
                    activeRequest.EmailSentAtUtc is not null);

                return new RolePromotionChallengeCreated
                {
                    RequestId = activeRequest.Id,
                    Token = CreateToken(activeRequest.Id),
                    ExpiresAtUtc = activeRequest.ExpiresAtUtc,
                    ReusedExisting = true,
                    EmailDeliveryPending = activeRequest.EmailSentAtUtc is null
                        && !IsEmailDeliveryInFlight(activeRequest, utcNow),
                    DeliverySuppressed = false
                };
            }

            // Persistent per-identity cooldown. The same-role case is already answered by the reuse
            // above (no new e-mail), but a request for the other role would otherwise deliver a
            // second message immediately. No account state is disclosed: the caller receives the
            // same generic pending response as a normal request.
            var cooldown = _identityCooldown;
            if (cooldown > TimeSpan.Zero)
            {
                var cooldownThreshold = utcNow - cooldown;
                var recentlyDelivered = await _dbContext.RolePromotionRequests
                    .AsNoTracking()
                    .Where(x => x.UserId == data.UserId
                                && x.ConsumedAtUtc == null
                                && x.SupersededAtUtc == null
                                && ((x.EmailSentAtUtc != null && x.EmailSentAtUtc > cooldownThreshold)
                                    || (x.EmailSendingAtUtc != null && x.EmailSendingAtUtc > cooldownThreshold)))
                    .OrderByDescending(x => x.EmailSentAtUtc ?? x.EmailSendingAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (recentlyDelivered is not null)
                {
                    if (ownsTransaction)
                    {
                        await _dbContext.Database.CommitTransactionAsync(cancellationToken);
                    }

                    _logger.LogInformation(
                        "{EventType} | Role promotion delivery suppressed by identity cooldown | RequestId={RequestId} | IdentityKey={IdentityKey} | Role={Role} | CooldownSeconds={CooldownSeconds}",
                        "ROLE_PROMOTION_DELIVERY_SUPPRESSED",
                        recentlyDelivered.Id,
                        ComputeIdentityKey(normalizedEmail),
                        data.TargetRole,
                        (int)cooldown.TotalSeconds);

                    return new RolePromotionChallengeCreated
                    {
                        RequestId = recentlyDelivered.Id,
                        // The suppressed request belongs to another role of the same identity. Its
                        // link is never handed to this caller; the delivery job derives it from the
                        // persisted request when it sends again.
                        Token = string.Empty,
                        ExpiresAtUtc = recentlyDelivered.ExpiresAtUtc,
                        ReusedExisting = true,
                        EmailDeliveryPending = false,
                        DeliverySuppressed = true
                    };
                }
            }

            // Only an expired request (or none) can reach this point. Its link is already unusable,
            // so replacing it cannot destroy anything the owner could still confirm.
            foreach (var expired in pendingRequests)
            {
                expired.SupersededAtUtc = utcNow;
                expired.ClaimedAtUtc = null;
                expired.ClaimedBy = null;
                expired.MarkAsUpdated();
            }

            if (pendingRequests.Count > 0)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var request = new RolePromotionRequest
            {
                UserId = data.UserId,
                NormalizedEmail = normalizedEmail,
                TargetRole = data.TargetRole,
                PasswordHash = data.PasswordHash,
                FullName = string.IsNullOrWhiteSpace(data.FullName) ? null : data.FullName.Trim(),
                ExpiresAtUtc = utcNow.Add(ChallengeTtl),
                ConcurrencyVersion = 0
            };

            // The token is derived from the request id, so the hash must be computed after the id
            // exists. BaseEntity assigns a v7 GUID in its constructor.
            var rawToken = CreateToken(request.Id);
            request.TokenHash = HashToken(rawToken);

            _dbContext.RolePromotionRequests.Add(request);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction)
            {
                await _dbContext.Database.CommitTransactionAsync(cancellationToken);
            }

            _logger.LogInformation(
                "{EventType} | Role promotion challenge created | RequestId={RequestId} | IdentityKey={IdentityKey} | Role={Role}",
                "ROLE_PROMOTION_REQUESTED",
                request.Id,
                ComputeIdentityKey(normalizedEmail),
                request.TargetRole);

            return new RolePromotionChallengeCreated
            {
                RequestId = request.Id,
                Token = rawToken,
                ExpiresAtUtc = request.ExpiresAtUtc,
                ReusedExisting = false,
                EmailDeliveryPending = true,
                DeliverySuppressed = false
            };
        }
        catch
        {
            if (ownsTransaction)
            {
                try
                {
                    await _dbContext.Database.RollbackTransactionAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    // The original failure is what callers must see; a rollback that already
                    // happened implicitly must not mask it.
                    _logger.LogWarning(
                        rollbackException,
                        "{EventType} | Role promotion creation rollback failed | UserId={UserId} | Role={Role}",
                        "ROLE_PROMOTION_CREATE_ROLLBACK_FAILED",
                        data.UserId,
                        data.TargetRole);
                }
            }

            throw;
        }
    }

    public async Task<RolePromotionChallengeValidation> ValidateAndConsumeAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadRequestId(token, out var requestId))
        {
            return new RolePromotionChallengeValidation { Status = RolePromotionChallengeStatus.Invalid };
        }

        var tokenHash = HashToken(token.Trim());
        var request = await _dbContext.RolePromotionRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null || !HashMatches(request.TokenHash, tokenHash))
        {
            return new RolePromotionChallengeValidation { Status = RolePromotionChallengeStatus.Invalid };
        }

        if (request.SupersededAtUtc is not null)
        {
            return new RolePromotionChallengeValidation
            {
                Status = RolePromotionChallengeStatus.Superseded,
                Request = request
            };
        }

        if (request.ConsumedAtUtc is not null)
        {
            return new RolePromotionChallengeValidation
            {
                Status = RolePromotionChallengeStatus.AlreadyConsumed,
                Request = request
            };
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.ExpiresAtUtc <= utcNow)
        {
            return new RolePromotionChallengeValidation
            {
                Status = RolePromotionChallengeStatus.Expired,
                Request = request
            };
        }

        // The claim attempt id is the proof the caller must present to MarkAppliedAsync. It is
        // generated before the conditional write so the row records the exact attempt that holds the
        // lease; a stale attempt can never close a challenge that was taken over.
        var claimAttemptId = Guid.CreateVersion7();
        var leaseThreshold = utcNow - ClaimLease;

        if (_dbContext.Database.IsRelational())
        {
            var claimed = await _dbContext.RolePromotionRequests
                .Where(x => x.Id == requestId
                            && x.ConsumedAtUtc == null
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
                // Another attempt claimed it, or the row was applied/superseded/expired between the
                // read and the conditional update. Record: a failed statement would abort the
                // surrounding transaction, so re-read only after the update (which is safe) to
                // classify the reason without ever guessing.
                var current = await _dbContext.RolePromotionRequests
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

                return new RolePromotionChallengeValidation
                {
                    Status = ClassifyUnclaimable(current, utcNow),
                    Request = current ?? request
                };
            }
        }
        else
        {
            // Non-relational providers (tests) persist the same lease through the tracked entity, so
            // MarkAppliedAsync can validate it the same way. The conditional check and the
            // concurrency token serialize two simultaneous attempts; the retry after a failure must
            // release the claim explicitly, exactly like the relational flow rolls it back.
            var tracked = await _dbContext.RolePromotionRequests
                .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

            if (tracked is null
                || tracked.ConsumedAtUtc is not null
                || tracked.SupersededAtUtc is not null
                || tracked.ExpiresAtUtc <= utcNow
                || (tracked.ClaimedAtUtc is { } claimedAt && claimedAt >= leaseThreshold))
            {
                return new RolePromotionChallengeValidation
                {
                    Status = ClassifyUnclaimable(tracked, utcNow),
                    Request = tracked ?? request
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
                // Another attempt claimed it first; that attempt owns the lease.
                _dbContext.ChangeTracker.Clear();
                var current = await _dbContext.RolePromotionRequests
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

                return new RolePromotionChallengeValidation
                {
                    Status = ClassifyUnclaimable(current, utcNow),
                    Request = current ?? request
                };
            }
        }

        _logger.LogInformation(
            "{EventType} | Role promotion challenge claimed | RequestId={RequestId} | UserId={UserId} | Role={Role}",
            "ROLE_PROMOTION_CHALLENGE_CLAIMED",
            request.Id,
            request.UserId,
            request.TargetRole);

        return new RolePromotionChallengeValidation
        {
            Status = RolePromotionChallengeStatus.Valid,
            Request = request,
            ClaimAttemptId = claimAttemptId
        };
    }

    public async Task<bool> MarkAppliedAsync(
        Guid requestId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default)
    {
        if (claimAttemptId == Guid.Empty)
        {
            return false;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var leaseThreshold = utcNow - ClaimLease;

        if (_dbContext.Database.IsRelational())
        {
            // Only the attempt that still holds a live lease may close the challenge. A lease that
            // expired (crashed attempt) or was taken over by another attempt must fail closed, even
            // if this caller already wrote the role/credential.
            var updated = await _dbContext.RolePromotionRequests
                .Where(x => x.Id == requestId
                            && x.ConsumedAtUtc == null
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

        var request = await _dbContext.RolePromotionRequests
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null
            || request.ConsumedAtUtc is not null
            || request.SupersededAtUtc is not null
            || request.ClaimedBy != claimAttemptId
            || request.ClaimedAtUtc is not { } claimedAt
            || claimedAt < leaseThreshold)
        {
            return false;
        }

        request.ConsumedAtUtc = utcNow;
        request.ClaimedAtUtc = null;
        request.ClaimedBy = null;
        request.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another consumer applied it first; the caller must reload instead of reporting success.
            return false;
        }
    }

    public async Task ReleaseClaimAsync(
        Guid requestId,
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
            await _dbContext.RolePromotionRequests
                .Where(x => x.Id == requestId
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

        var request = await _dbContext.RolePromotionRequests
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null
            || request.ConsumedAtUtc is not null
            || request.ClaimedBy != claimAttemptId)
        {
            return;
        }

        request.ClaimedAtUtc = null;
        request.ClaimedBy = null;
        request.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt already took over the lease or consumed the request.
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    public Task<RolePromotionRequest?> FindAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        return _dbContext.RolePromotionRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    }

    public async Task<RolePromotionEmailDelivery?> TryClaimEmailDeliveryAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var attemptId = Guid.CreateVersion7();

        if (_dbContext.Database.IsRelational())
        {
            var snapshot = await FindAsync(requestId, cancellationToken);
            if (!IsEmailDeliverable(snapshot, utcNow))
            {
                return null;
            }

            var leaseThreshold = utcNow - EmailSendLease;
            var claimed = await _dbContext.RolePromotionRequests
                .Where(x => x.Id == requestId
                            && x.EmailSentAtUtc == null
                            && x.ConsumedAtUtc == null
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

        // InMemory used by unit/integration tests: a tracked read plus save emulates the lease. Two
        // simultaneous in-process jobs are still serialized by the conditional state check, and the
        // relational concurrency guarantees are covered by the PostgreSQL tests.
        var request = await _dbContext.RolePromotionRequests
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (!IsEmailDeliverable(request, utcNow)
            || (request!.EmailSendingAtUtc is { } sendingAt && sendingAt >= utcNow - EmailSendLease))
        {
            return null;
        }

        request.EmailSendingAtUtc = utcNow;
        request.EmailSendAttemptId = attemptId;
        request.ConcurrencyVersion++;
        request.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt claimed the lease first; that attempt owns the delivery.
            return null;
        }

        return BuildDelivery(request, attemptId);
    }

    public async Task MarkEmailSentAsync(
        Guid requestId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (_dbContext.Database.IsRelational())
        {
            // Only the attempt that still owns the lease may record the send. A late worker whose
            // lease already expired cannot overwrite a newer delivery.
            await _dbContext.RolePromotionRequests
                .Where(x => x.Id == requestId
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

        var request = await _dbContext.RolePromotionRequests
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null
            || request.EmailSentAtUtc is not null
            || request.EmailSendAttemptId != attemptId)
        {
            return;
        }

        request.EmailSentAtUtc = utcNow;
        request.EmailSendingAtUtc = null;
        request.EmailSendAttemptId = null;
        request.MarkAsUpdated();

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
        Guid requestId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (_dbContext.Database.IsRelational())
        {
            await _dbContext.RolePromotionRequests
                .Where(x => x.Id == requestId
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

        var request = await _dbContext.RolePromotionRequests
            .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

        if (request is null
            || request.EmailSentAtUtc is not null
            || request.EmailSendAttemptId != attemptId)
        {
            return;
        }

        request.EmailSendingAtUtc = null;
        request.EmailSendAttemptId = null;
        request.MarkAsUpdated();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another attempt already took over the lease.
        }
    }

    public async Task<IReadOnlyList<Guid>> FindPendingEmailRequestIdsAsync(
        TimeSpan minimumAge,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var createdBefore = utcNow.Subtract(minimumAge < TimeSpan.Zero ? TimeSpan.Zero : minimumAge);
        var leaseThreshold = utcNow.Subtract(leaseDuration < TimeSpan.Zero ? TimeSpan.Zero : leaseDuration);

        return await _dbContext.RolePromotionRequests
            .AsNoTracking()
            .Where(x => x.EmailSentAtUtc == null
                        && x.ConsumedAtUtc == null
                        && x.SupersededAtUtc == null
                        && x.ExpiresAtUtc > utcNow
                        && x.CreatedAtUtc <= createdBefore
                        // Never enqueue a request whose delivery lease is still valid: the running
                        // job may be between the SMTP accept and the EmailSentAtUtc update.
                        && (x.EmailSendingAtUtc == null || x.EmailSendingAtUtc < leaseThreshold))
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    private static bool IsEmailDeliverable(RolePromotionRequest? request, DateTime utcNow)
        => request is not null
           && request.ConsumedAtUtc is null
           && request.SupersededAtUtc is null
           && request.EmailSentAtUtc is null
           && request.ExpiresAtUtc > utcNow;

    /// <summary>
    /// A request whose delivery lease is still valid is being sent by another worker; enqueueing it
    /// again would only produce a job that cannot claim the lease.
    /// </summary>
    private static bool IsEmailDeliveryInFlight(RolePromotionRequest request, DateTime utcNow)
        => request.EmailSendingAtUtc is { } sendingAt
           && sendingAt >= utcNow - EmailSendLease;

    private RolePromotionEmailDelivery BuildDelivery(RolePromotionRequest request, Guid attemptId)
        => new()
        {
            RequestId = request.Id,
            AttemptId = attemptId,
            Email = request.NormalizedEmail,
            TargetRole = request.TargetRole,
            Token = CreateToken(request.Id),
            ExpiresAtUtc = request.ExpiresAtUtc
        };

    private async Task AcquireCreationLockAsync(
        Guid userId,
        UserRoleType targetRole,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        // Transaction-scoped advisory lock: released automatically on commit/rollback, so it cannot
        // leak if the request fails. The key includes the identity and the target role so unrelated
        // promotions never block each other.
        var lockKey = $"role-promotion:{userId:N}:{targetRole}";
        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text, 0))",
            [lockKey],
            cancellationToken);
    }

    private static RolePromotionChallengeStatus ClassifyUnclaimable(
        RolePromotionRequest? current,
        DateTime utcNow)
    {
        if (current is null)
        {
            return RolePromotionChallengeStatus.Invalid;
        }

        if (current.SupersededAtUtc is not null)
        {
            return RolePromotionChallengeStatus.Superseded;
        }

        if (current.ConsumedAtUtc is not null)
        {
            return RolePromotionChallengeStatus.AlreadyConsumed;
        }

        if (current.ExpiresAtUtc <= utcNow)
        {
            return RolePromotionChallengeStatus.Expired;
        }

        // A live lease: another confirmation attempt is applying the role right now.
        return RolePromotionChallengeStatus.AlreadyConsumed;
    }

    internal static string HashToken(string rawToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>
    /// Non-reversible identifier of an identity for operational logs. It is an HMAC under the
    /// dedicated promotion key with a purpose prefix, so the same e-mail always maps to the same
    /// value inside one deployment while the raw address (and the token) never reaches the logs and
    /// the value cannot be reversed or compared against a precomputed table without the key.
    /// </summary>
    internal string ComputeIdentityKey(string normalizedEmail)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(IdentityKeyPurpose + normalizedEmail));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Derives the opaque token for a request: the request id followed by its HMAC-SHA256. Nothing
    /// secret is stored, and only a holder of the server signing key can rebuild the value, which
    /// is what allows the e-mail job to retry after a crash or a cache loss.
    /// </summary>
    internal string CreateToken(Guid requestId)
    {
        Span<byte> buffer = stackalloc byte[TokenLength];
        requestId.TryWriteBytes(buffer[..RequestIdLength]);
        ComputeMac(buffer[..RequestIdLength], buffer[RequestIdLength..]);
        return Base64UrlEncode(buffer);
    }

    private bool TryReadRequestId(string token, out Guid requestId)
    {
        requestId = Guid.Empty;
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
        ComputeMac(bytes.AsSpan(0, RequestIdLength), expectedMac);

        if (!CryptographicOperations.FixedTimeEquals(expectedMac, bytes.AsSpan(RequestIdLength, MacLength)))
        {
            return false;
        }

        requestId = new Guid(bytes.AsSpan(0, RequestIdLength));
        return true;
    }

    private void ComputeMac(ReadOnlySpan<byte> requestId, Span<byte> destination)
    {
        Span<byte> input = stackalloc byte[TokenPurpose.Length + RequestIdLength];
        Encoding.UTF8.GetBytes(TokenPurpose, input);
        requestId.CopyTo(input[TokenPurpose.Length..]);

        using var hmac = new HMACSHA256(_signingKey);
        if (!hmac.TryComputeHash(input, destination, out _))
        {
            throw new InvalidOperationException("Could not derive the role promotion token.");
        }
    }

    private static bool HashMatches(string storedHash, string computedHash)
    {
        var stored = Encoding.ASCII.GetBytes(storedHash);
        var computed = Encoding.ASCII.GetBytes(computedHash);
        return stored.Length == computed.Length
               && CryptographicOperations.FixedTimeEquals(stored, computed);
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
