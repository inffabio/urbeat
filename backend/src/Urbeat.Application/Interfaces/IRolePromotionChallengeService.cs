using Urbeat.Domain.Entities;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Material needed to create a pending role promotion request. The caller is responsible for
/// producing the password hash; the plain password never reaches this contract.
/// </summary>
public sealed class RolePromotionRequestData
{
    public Guid UserId { get; init; }

    public string Email { get; init; } = string.Empty;

    public UserRoleType TargetRole { get; init; }

    public string PasswordHash { get; init; } = string.Empty;

    public string? FullName { get; init; }
}

public enum RolePromotionChallengeStatus
{
    Valid = 1,
    AlreadyConsumed = 2,
    Expired = 3,
    Superseded = 4,
    Invalid = 5
}

public sealed class RolePromotionChallengeValidation
{
    public RolePromotionChallengeStatus Status { get; init; }

    public RolePromotionRequest? Request { get; init; }

    /// <summary>
    /// Identifies the claim taken by this attempt. The caller must present the same value to
    /// <see cref="IRolePromotionChallengeService.MarkAppliedAsync"/>; a lease that expired or was
    /// taken over by another attempt is rejected instead of closing the challenge.
    /// </summary>
    public Guid? ClaimAttemptId { get; init; }

    public bool IsValid => Status == RolePromotionChallengeStatus.Valid;
}

public sealed class RolePromotionChallengeCreated
{
    public Guid RequestId { get; init; }

    /// <summary>
    /// Opaque token derived from the request id and the server signing key. It is not stored
    /// anywhere; the e-mail job derives the same value again when it renders the link.
    /// </summary>
    public string Token { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// True when an already active request for the same identity/role was reused instead of being
    /// replaced. The link that was already delivered stays valid and its password is never changed.
    /// </summary>
    public bool ReusedExisting { get; init; }

    /// <summary>
    /// True when the request still needs its e-mail delivered. A request that was already sent (or
    /// is currently being sent) must not be enqueued again.
    /// </summary>
    public bool EmailDeliveryPending { get; init; }

    /// <summary>
    /// True when the per-identity cooldown suppressed the delivery. The returned request belongs to
    /// a recent promotion of the same identity and must not be re-sent by this call.
    /// </summary>
    public bool DeliverySuppressed { get; init; }
}

/// <summary>
/// Everything the e-mail job needs to build the confirmation link for a still-sendable request.
/// </summary>
public sealed class RolePromotionEmailDelivery
{
    public Guid RequestId { get; init; }

    /// <summary>
    /// Identifies the delivery lease. The job must present it to record the send or release the
    /// lease, so a late attempt cannot overwrite a newer one.
    /// </summary>
    public Guid AttemptId { get; init; }

    public string Email { get; init; } = string.Empty;

    public UserRoleType TargetRole { get; init; }

    public string Token { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}

/// <summary>
/// Issues, replaces and atomically consumes single-purpose role promotion challenges. This is
/// deliberately not shared with e-mail confirmation or e-mail change tokens.
/// </summary>
public interface IRolePromotionChallengeService
{
    /// <summary>
    /// Creates or reuses the single pending request for the identity/target role. Concurrent
    /// callers are serialized so only one pending row survives.
    /// </summary>
    /// <remarks>
    /// A request that is still active is reused instead of superseded: a repeated registration can
    /// never invalidate the link that was already delivered nor change the pending password. Only an
    /// expired request is replaced, and a delivered promotion for another role of the same identity
    /// is not re-sent before the configured cooldown, which bounds mailbox flooding.
    /// </remarks>
    Task<RolePromotionChallengeCreated> CreateAsync(
        RolePromotionRequestData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the token and takes a short lease on the request so a single caller may apply the
    /// role and credential. The challenge is only marked consumed by <see cref="MarkAppliedAsync"/>
    /// after the role and credential were written, so a failed attempt can be retried.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="RolePromotionChallengeValidation.ClaimAttemptId"/> is the proof of the
    /// lease. An attempt whose lease expired or was taken over by another caller must not mark the
    /// request as applied.
    /// </remarks>
    Task<RolePromotionChallengeValidation> ValidateAndConsumeAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the request as applied only when <paramref name="claimAttemptId"/> still owns a valid
    /// lease (<see cref="RolePromotionRequest.ClaimedAtUtc"/> and
    /// <see cref="RolePromotionRequest.ClaimedBy"/>). Returns <see langword="false"/> when the lease
    /// expired, was taken over, or the request was already consumed, so the caller must not report
    /// success.
    /// </summary>
    Task<bool> MarkAppliedAsync(
        Guid requestId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the confirmation lease when the attempt fails before applying anything. A stale
    /// attempt id is ignored, so it cannot release a lease taken over by another attempt.
    /// </summary>
    Task ReleaseClaimAsync(
        Guid requestId,
        Guid claimAttemptId,
        CancellationToken cancellationToken = default);

    Task<RolePromotionRequest?> FindAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a short delivery lease and returns the material (including the re-derived token) when
    /// the request is still sendable and no other attempt holds a valid lease. Returns
    /// <see langword="null"/> when the request expired, was superseded/applied, was already sent or
    /// another attempt is delivering it right now.
    /// </summary>
    /// <remarks>
    /// The lease makes the job and the recovery sweep idempotent per request id: a duplicate or late
    /// enqueue observes the active lease and does not send again.
    /// </remarks>
    Task<RolePromotionEmailDelivery?> TryClaimEmailDeliveryAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the e-mail was handed to the provider. Only the attempt that holds the current
    /// lease may record it; the operation is idempotent and never resets an already sent request.
    /// </summary>
    Task MarkEmailSentAsync(
        Guid requestId,
        Guid attemptId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a delivery lease after a failed send so a later job can try again immediately. A
    /// stale attempt id is ignored, so it cannot clear a newer lease.
    /// </summary>
    Task ReleaseEmailSendLeaseAsync(
        Guid requestId,
        Guid attemptId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of pending requests whose e-mail was never recorded as sent and whose delivery lease is
    /// absent or expired. Used by the recovery job to re-enqueue delivery without depending on the
    /// original enqueue, on any cache entry or on a possibly still-running worker.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindPendingEmailRequestIdsAsync(
        TimeSpan minimumAge,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}
