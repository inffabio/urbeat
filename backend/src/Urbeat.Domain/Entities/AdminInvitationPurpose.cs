namespace Urbeat.Domain.Entities;

/// <summary>
/// Purpose of an administrative e-mail link. The purpose is part of the token derivation domain,
/// so an invitation token is never accepted by a password reset endpoint (and vice versa).
/// </summary>
public enum AdminInvitationPurpose
{
    AccountInvitation = 1,
    PasswordReset = 2
}
