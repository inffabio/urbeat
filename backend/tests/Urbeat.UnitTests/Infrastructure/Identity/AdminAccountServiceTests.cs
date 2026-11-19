using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Repositories;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.ReadRepositories;
using Urbeat.Infrastructure.Persistence.Repositories;
using Urbeat.Infrastructure.Persistence.UnitOfWork;

namespace Urbeat.UnitTests.Infrastructure.Identity;

/// <summary>
/// Server-side invariants of administrative account management: invite-only creation, one active
/// link per target/purpose, session revocation, self-action protection and the rule that the
/// platform can never be left without an active administrator.
/// </summary>
public sealed class AdminAccountServiceTests
{
    private const string AdminEmail = "admin@urbeat.local";
    private const string OtherAdminEmail = "outro.admin@urbeat.local";
    private const string AdminPassword = "Admin12345";
    private const string OtherPassword = "OutroAdmin123";
    private const string SigningKey = "urbeat-unit-tests-admin-account-signing-key-2026";

    [Fact]
    public async Task InviteAsync_ShouldCreateAnInvitedAdministrator_WithoutPassword_AndWithOneActiveLink()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IAdminInvitationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var result = await service.InviteAsync(
            "  Novo.Admin@Exemplo.com ",
            actor!.Id,
            "203.0.113.10");

        result.Succeeded.Should().BeTrue();
        result.Administrator!.Email.Should().Be("novo.admin@exemplo.com");
        result.Administrator.Status.Should().Be("Invited");

        var created = await userManager.FindByEmailAsync("novo.admin@exemplo.com");
        created.Should().NotBeNull();
        created!.EmailConfirmed.Should().BeFalse();
        created.PasswordHash.Should().BeNull("an invitation must not create a password");
        (await userManager.IsInRoleAsync(created, "Admin")).Should().BeTrue();

        var invitations = await db.AdminInvitations
            .Where(x => x.UserId == created.Id)
            .ToListAsync();
        invitations.Should().HaveCount(1);
        invitations[0].Purpose.Should().Be(AdminInvitationPurpose.AccountInvitation);
        invitations[0].ExpiresAtUtc.Should().BeCloseTo(
            DateTime.UtcNow.AddHours(24),
            TimeSpan.FromMinutes(2));
        invitations[0].TokenHash.Should().NotContain(
            invitationService.CreateToken(invitations[0].Id, invitations[0].Purpose));

        var audit = await db.AuditLogs.SingleAsync(x => x.Event == "AdminInvitationSent");
        audit.UserId.Should().Be(actor.Id);
        audit.EntityId.Should().Be(created.Id);
        audit.Description.Should().NotContain("token");
        audit.Description.Should().NotContain(invitationService.CreateToken(invitations[0].Id, invitations[0].Purpose));
    }

    [Fact]
    public async Task InviteAsync_ShouldRejectAnAddressThatAlreadyBelongsToAnyIdentity()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var result = await service.InviteAsync("cliente@urbeat.local", actor!.Id, null);

        result.Status.Should().Be(AdminAccountOperationStatus.EmailAlreadyInUse);
        result.Code.Should().Be(AdminAccountErrorCodes.EmailAlreadyInUse);
        result.Error.Should().Be("Este e-mail já está em uso.");
        (await db.Users.CountAsync()).Should().Be(3, "no identity may be created for a duplicated address");
        (await db.AdminInvitations.CountAsync()).Should().Be(0, "no invitation may be created for a duplicated address");
        (await db.AuditLogs.SingleAsync(x => x.Event == "AdminActionDenied")).Description
            .Should().Contain(AdminAccountErrorCodes.EmailAlreadyInUse);
    }

    [Fact]
    public async Task InviteAsync_ShouldRejectASellerAddress_WithASpecificCode_AndChangeNothing()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        const string sellerEmail = "lojista.convite@urbeat.local";
        var seller = new IdentityUser<Guid>
        {
            UserName = sellerEmail,
            Email = sellerEmail,
            EmailConfirmed = true
        };
        (await userManager.CreateAsync(seller, "Lojista123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(seller, "Seller")).Succeeded.Should().BeTrue();

        var usersBefore = await db.Users.CountAsync();
        var userRolesBefore = await db.UserRoles.CountAsync();
        var invitationsBefore = await db.AdminInvitations.CountAsync();

        var result = await service.InviteAsync(sellerEmail, actor!.Id, "203.0.113.10");

        result.Status.Should().Be(AdminAccountOperationStatus.SellerEmailAlreadyInUse);
        result.Code.Should().Be(AdminAccountErrorCodes.SellerEmailAlreadyInUse);
        result.Error.Should().Be(
            "Este e-mail já pertence a uma conta de lojista. O convite de administrador não pode ser enviado.");

        (await db.Users.CountAsync()).Should().Be(
            usersBefore,
            "a rejected invitation must not create an identity");
        (await db.UserRoles.CountAsync()).Should().Be(
            userRolesBefore,
            "a rejected invitation must not grant a role");
        (await db.AdminInvitations.CountAsync()).Should().Be(
            invitationsBefore,
            "a rejected invitation must not create an invitation row");
        (await userManager.IsInRoleAsync(seller, "Seller")).Should().BeTrue();
        (await userManager.IsInRoleAsync(seller, "Admin")).Should().BeFalse();

        var audit = await db.AuditLogs.SingleAsync(x => x.Event == "AdminActionDenied");
        audit.EntityId.Should().Be(seller.Id);
        audit.Description.Should().Contain(AdminAccountErrorCodes.SellerEmailAlreadyInUse);
    }

    [Fact]
    public async Task ResendInvitationAsync_ShouldReuseTheSameLink_WhileItIsPending()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "pendente@exemplo.com");
        var invitationId = invitation.Invitation!.Id;

        var result = await service.ResendInvitationAsync(invitation.Administrator!.Id, actor.Id, null);

        result.Succeeded.Should().BeTrue();
        result.InvitationId.Should().Be(invitationId);
        (await db.AdminInvitations.CountAsync(x => x.UserId == invitation.Administrator.Id)).Should().Be(1);
        (await db.AdminInvitations.SingleAsync()).SupersededAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ResendInvitationAsync_ShouldSupersedeAnExpiredLink_AndIssueANewOne()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "expirado@exemplo.com");
        var firstId = invitation.Invitation!.Id;

        var stored = await db.AdminInvitations.SingleAsync(x => x.Id == firstId);
        stored.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var result = await service.ResendInvitationAsync(invitation.Administrator!.Id, actor.Id, null);

        result.Succeeded.Should().BeTrue();
        result.InvitationId.Should().NotBe(firstId);

        var rows = await db.AdminInvitations.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].SupersededAtUtc.Should().NotBeNull();
        rows[1].SupersededAtUtc.Should().BeNull();
        rows[1].ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task CancelInvitationAsync_ShouldRevokeTheLink_AndDeleteTheNeverActivatedIdentity()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "cancelar@exemplo.com");
        var targetId = invitation.Administrator!.Id;

        var result = await service.CancelInvitationAsync(targetId, actor.Id, null);

        result.Succeeded.Should().BeTrue();
        (await db.Users.AnyAsync(x => x.Id == targetId)).Should().BeFalse();

        var row = await db.AdminInvitations.SingleAsync();
        row.RevokedAtUtc.Should().NotBeNull();
        row.UserId.Should().BeNull();
        (await db.AuditLogs.SingleAsync(x => x.Event == "AdminInvitationCancelled")).Description
            .Should().Contain("deleted");

        var repeated = await service.CancelInvitationAsync(targetId, actor.Id, null);
        repeated.Status.Should().Be(AdminAccountOperationStatus.InvitationNotPending);
        repeated.Code.Should().Be(AdminAccountErrorCodes.InvitationNotPending);
    }

    [Fact]
    public async Task CancelInvitationAsync_ShouldRejectAnAccountThatWasAlreadyActivated()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);

        var result = await service.CancelInvitationAsync(other!.Id, actor!.Id, null);

        result.Status.Should().Be(AdminAccountOperationStatus.NotActive);
        result.Code.Should().Be(AdminAccountErrorCodes.AdminNotActive);
    }

    [Fact]
    public async Task UpdateEmailAsync_ShouldReissueTheInvitation_WhenTheTargetIsInvited()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "antigo@exemplo.com");
        var oldInvitationId = invitation.Invitation!.Id;

        var result = await service.UpdateEmailAsync(
            invitation.Administrator!.Id,
            "Novo.Endereco@Exemplo.com",
            actor.Id,
            null);

        result.Succeeded.Should().BeTrue();
        result.Administrator!.Email.Should().Be("novo.endereco@exemplo.com");
        result.Administrator.Status.Should().Be("Invited");
        result.Invitation!.Id.Should().NotBe(oldInvitationId);

        var superseded = await db.AdminInvitations.SingleAsync(x => x.Id == oldInvitationId);
        superseded.SupersededAtUtc.Should().NotBeNull();
        (await db.AdminInvitations.CountAsync(x => x.UserId == invitation.Administrator.Id)).Should().Be(2);
    }

    [Fact]
    public async Task UpdateEmailAsync_ShouldRevokeSessionsAndRotateTheStamp_WhenTheTargetIsActive()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);

        var stampBefore = await userManager.GetSecurityStampAsync(other!);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = other.Id,
            Role = null,
            TokenHash = new string('b', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await service.UpdateEmailAsync(
            other.Id,
            "outro.novo@urbeat.local",
            actor!.Id,
            null);

        result.Succeeded.Should().BeTrue();
        result.Administrator!.Email.Should().Be("outro.novo@urbeat.local");
        result.Administrator.IsEmailConfirmed.Should().BeTrue();

        var updated = await userManager.FindByIdAsync(other.Id.ToString());
        (await userManager.GetSecurityStampAsync(updated!)).Should().NotBe(stampBefore);
        (await db.RefreshTokens.SingleAsync()).RevokedAtUtc.Should().NotBeNull();

        var audit = await db.AuditLogs.SingleAsync(x => x.Event == "AdminEmailChanged");
        audit.Description.Should().Contain(OtherAdminEmail).And.Contain("outro.novo@urbeat.local");
        audit.Description.Should().NotContain("token");
    }

    [Fact]
    public async Task BlockAsync_ShouldPersistTheState_RevokeSessionsAndAudit_WithoutLeakingSecrets()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);
        var stampBefore = await userManager.GetSecurityStampAsync(other!);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = other!.Id,
            Role = null,
            TokenHash = new string('c', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await service.BlockAsync(
            other.Id,
            "Desligamento do colaborador",
            actor!.Id,
            "203.0.113.10");

        result.Succeeded.Should().BeTrue();
        result.Administrator!.IsBlocked.Should().BeTrue();
        result.Administrator.Status.Should().Be("Blocked");

        var state = await db.AdminAccountStates.SingleAsync(x => x.UserId == other.Id);
        state.IsBlocked.Should().BeTrue();
        state.BlockedByAdminId.Should().Be(actor.Id);

        (await db.RefreshTokens.SingleAsync()).RevokedAtUtc.Should().NotBeNull();
        (await userManager.GetSecurityStampAsync(other)).Should().NotBe(stampBefore);

        var audit = await db.AuditLogs.SingleAsync(x => x.Event == "AdminBlocked");
        audit.UserId.Should().Be(actor.Id);
        audit.EntityId.Should().Be(other.Id);
        audit.IpAddress.Should().Be("203.0.113.10");
        audit.Description.Should().Contain("Desligamento do colaborador");
    }

    [Fact]
    public async Task BlockAsync_ShouldRejectSelfAction_RepeatedBlock_AndTheLastActiveAdmin()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);

        var self = await service.BlockAsync(actor!.Id, null, actor.Id, null);
        self.Status.Should().Be(AdminAccountOperationStatus.SelfActionNotAllowed);

        var first = await service.BlockAsync(other!.Id, null, actor.Id, null);
        first.Succeeded.Should().BeTrue();

        var repeated = await service.BlockAsync(other.Id, null, actor.Id, null);
        repeated.Status.Should().Be(AdminAccountOperationStatus.AlreadyBlocked);

        // The blocked administrator still holds a valid access token. It cannot remove the only
        // remaining active administrator.
        var lastActive = await service.RemoveAsync(actor.Id, null, other.Id, null);
        lastActive.Status.Should().Be(AdminAccountOperationStatus.LastActiveAdmin);
        (await db.AuditLogs.CountAsync(x => x.Event == "AdminActionDenied")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UnblockAsync_ShouldClearTheAdministrativeStateAndTheIdentityLockout()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);

        await service.BlockAsync(other!.Id, null, actor!.Id, null);
        await userManager.SetLockoutEndDateAsync(other, DateTimeOffset.UtcNow.AddMinutes(10));

        var result = await service.UnblockAsync(other.Id, actor.Id, null);

        result.Succeeded.Should().BeTrue();
        result.Administrator!.IsBlocked.Should().BeFalse();

        var state = await db.AdminAccountStates.SingleAsync();
        state.IsBlocked.Should().BeFalse();
        state.BlockedAtUtc.Should().BeNull();

        var updated = await userManager.FindByIdAsync(other.Id.ToString());
        (await userManager.IsLockedOutAsync(updated!)).Should().BeFalse();

        var notBlocked = await service.UnblockAsync(other.Id, actor.Id, null);
        notBlocked.Status.Should().Be(AdminAccountOperationStatus.NotBlocked);
    }

    [Fact]
    public async Task RemoveAsync_ShouldPreserveTheIdentityAndOtherRoles_WhenTheAccountIsActive()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var seller = new IdentityUser<Guid>
        {
            UserName = "lojista.admin@urbeat.local",
            Email = "lojista.admin@urbeat.local",
            EmailConfirmed = true
        };
        (await userManager.CreateAsync(seller, "Lojista123!")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(seller, "Admin")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(seller, "Seller")).Succeeded.Should().BeTrue();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = seller.Id,
            Role = "Seller",
            TokenHash = new string('d', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await service.RemoveAsync(seller.Id, "mudança de função", actor!.Id, null);

        result.Succeeded.Should().BeTrue();
        (await db.Users.AnyAsync(x => x.Id == seller.Id)).Should().BeTrue(
            "the identity and store data must be preserved");
        (await userManager.IsInRoleAsync(seller, "Admin")).Should().BeFalse();
        (await userManager.IsInRoleAsync(seller, "Seller")).Should().BeTrue();
        (await db.RefreshTokens.SingleAsync()).RevokedAtUtc.Should().NotBeNull();

        var audit = await db.AuditLogs.SingleAsync(x => x.Event == "AdminRemoved");
        audit.Description.Should().Contain("RoleRemoved");
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteANeverActivatedIdentity()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "remover@exemplo.com");

        var result = await service.RemoveAsync(invitation.Administrator!.Id, null, actor.Id, null);

        result.Succeeded.Should().BeTrue();
        (await db.Users.AnyAsync(x => x.Id == invitation.Administrator.Id)).Should().BeFalse();
        (await db.AuditLogs.SingleAsync(x => x.Event == "AdminRemoved")).Description
            .Should().Contain("PendingInvitationDeleted");
    }

    [Fact]
    public async Task RequestPasswordResetAsync_ShouldUseSixtyMinutes_RevokeSessions_AndNeverUnblock()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);

        await service.BlockAsync(other!.Id, null, actor!.Id, null);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = other.Id,
            Role = null,
            TokenHash = new string('e', 64),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await service.RequestPasswordResetAsync(other.Id, actor.Id, null);

        result.Succeeded.Should().BeTrue();
        var invitation = await db.AdminInvitations.SingleAsync();
        invitation.Purpose.Should().Be(AdminInvitationPurpose.PasswordReset);
        invitation.ExpiresAtUtc.Should().BeCloseTo(
            DateTime.UtcNow.AddMinutes(60),
            TimeSpan.FromMinutes(2));
        (await db.RefreshTokens.SingleAsync()).RevokedAtUtc.Should().NotBeNull();

        // Reusing the pending request must not generate a second link.
        var reused = await service.RequestPasswordResetAsync(other.Id, actor.Id, null);
        reused.InvitationId.Should().Be(invitation.Id);
        (await db.AdminInvitations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AcceptInvitationAsync_ShouldApplyThePassword_AndRejectReuse()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IAdminInvitationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "aceite@exemplo.com");
        var token = invitationService.CreateToken(
            invitation.Invitation!.Id,
            AdminInvitationPurpose.AccountInvitation);

        var validation = await service.ValidateInvitationTokenAsync(
            token,
            AdminInvitationPurpose.AccountInvitation);
        validation.Valid.Should().BeTrue();
        validation.Purpose.Should().Be("AccountInvitation");
        validation.EmailMasked.Should().Be("a***@exemplo.com");

        var result = await service.AcceptInvitationAsync(token, "NovaSenha123");

        result.Succeeded.Should().BeTrue();
        var created = await userManager.FindByEmailAsync("aceite@exemplo.com");
        created!.EmailConfirmed.Should().BeTrue();
        (await userManager.CheckPasswordAsync(created, "NovaSenha123")).Should().BeTrue();

        var stored = await db.AdminInvitations.SingleAsync();
        stored.ConsumedAtUtc.Should().NotBeNull();
        stored.TokenHash.Should().NotContain(token);

        var reused = await service.AcceptInvitationAsync(token, "OutraSenha123");
        reused.Succeeded.Should().BeFalse();
        reused.Code.Should().Be(AdminAccountErrorCodes.TokenAlreadyUsed);

        var swapped = await service.ValidateInvitationTokenAsync(
            token,
            AdminInvitationPurpose.PasswordReset);
        swapped.Valid.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptInvitationAsync_ShouldRejectALink_WhenTheTargetLostTheAdminRole()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IAdminInvitationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);

        var invitation = await InvitePendingAsync(service, actor!.Id, "removido@exemplo.com");
        var token = invitationService.CreateToken(
            invitation.Invitation!.Id,
            AdminInvitationPurpose.AccountInvitation);
        var target = await userManager.FindByEmailAsync("removido@exemplo.com");
        (await userManager.RemoveFromRoleAsync(target!, "Admin")).Succeeded.Should().BeTrue();

        var result = await service.AcceptInvitationAsync(token, "NovaSenha123");

        result.Succeeded.Should().BeFalse();
        result.Code.Should().Be(AdminAccountErrorCodes.InvalidOrExpiredToken);
        (await userManager.CheckPasswordAsync(target!, "NovaSenha123")).Should().BeFalse(
            "a removed administrator must not be able to consume a stale link");
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_ShouldKeepABlockedAdministratorBlocked()
    {
        await using var provider = await CreateProviderAsync();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IAdminInvitationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await userManager.FindByEmailAsync(AdminEmail);
        var other = await userManager.FindByEmailAsync(OtherAdminEmail);

        await service.BlockAsync(other!.Id, null, actor!.Id, null);
        var delivery = await service.RequestPasswordResetAsync(other.Id, actor.Id, null);
        var token = invitationService.CreateToken(
            delivery.InvitationId!.Value,
            AdminInvitationPurpose.PasswordReset);

        var result = await service.ConfirmPasswordResetAsync(token, "ResetSenha123");

        result.Succeeded.Should().BeTrue();
        var updated = await userManager.FindByIdAsync(other.Id.ToString());
        (await userManager.CheckPasswordAsync(updated!, "ResetSenha123")).Should().BeTrue();
        (await db.AdminAccountStates.SingleAsync(x => x.UserId == other.Id)).IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentBlocksOfTheLastTwoAdministrators_ShouldAllowExactlyOne()
    {
        await using var provider = await CreateProviderAsync();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var firstService = firstScope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IAdminAccountService>();
        var userManager = firstScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var first = await userManager.FindByEmailAsync(AdminEmail);
        var second = await userManager.FindByEmailAsync(OtherAdminEmail);

        var results = await Task.WhenAll(
            firstService.BlockAsync(second!.Id, null, first!.Id, "203.0.113.10"),
            secondService.BlockAsync(first.Id, null, second.Id, "203.0.113.11"));

        results.Count(result => result.Succeeded).Should().Be(1);
        results.Count(result => result.Status == AdminAccountOperationStatus.LastActiveAdmin).Should().Be(1);

        using var checkScope = provider.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AdminAccountStates.CountAsync(state => state.IsBlocked)).Should().Be(1);
    }

    private static async Task<AdminAccountOperationResult> InvitePendingAsync(
        IAdminAccountService service,
        Guid actorId,
        string email)
    {
        var result = await service.InviteAsync(email, actorId, null);
        result.Succeeded.Should().BeTrue("the invitation must succeed for the test to proceed");
        return result;
    }

    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();
    private const string DatabaseName = "urbeat-admin-accounts-shared";

    private static async Task<ServiceProvider> CreateProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(DatabaseName, DatabaseRoot));
        services.AddIdentityCore<IdentityUser<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenHasher, TokenHasher>();
        services.Configure<RolePromotionChallengeOptions>(options => options.SigningKey = SigningKey);
        services.Configure<AdminAccountOptions>(_ => { });
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEfUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAdminInvitationService, AdminInvitationService>();
        services.AddScoped<IAdminAccountReadRepository, AdminAccountReadRepository>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddSingleton(Mock.Of<IBackgroundJobClient>());

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

        foreach (var roleName in new[] { "Admin", "Customer", "Seller" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }
        }

        await CreateUserAsync(userManager, AdminEmail, AdminPassword, "Admin");
        await CreateUserAsync(userManager, OtherAdminEmail, OtherPassword, "Admin");
        await CreateUserAsync(userManager, "cliente@urbeat.local", "Cliente123!", "Customer");

        return provider;
    }

    private static async Task CreateUserAsync(
        UserManager<IdentityUser<Guid>> userManager,
        string email,
        string password,
        string role)
    {
        var user = new IdentityUser<Guid>
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
    }
}
