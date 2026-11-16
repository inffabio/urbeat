namespace Urbeat.Domain.Entities;

/// <summary>
/// Derived state of an administrative e-mail link. It is never persisted: it is computed from the
/// timestamps and the current UTC clock.
/// </summary>
public enum AdminInvitationState
{
    Pending,
    Sending,
    Expired,
    Accepted,
    Revoked,
    Superseded
}
