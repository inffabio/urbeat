using System.Text.Json.Serialization;

namespace Urbeat.Application.DTOs;

/// <summary>Query accepted by the paginated administrator listing.</summary>
public sealed class AdminAccountsQueryDto
{
    /// <summary><c>all</c> (default), <c>active</c>, <c>blocked</c> or <c>invited</c>.</summary>
    public string? Status { get; init; }

    /// <summary>Case-insensitive partial e-mail search, at most 120 characters.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed class AdminInvitationDto
{
    public Guid Id { get; init; }

    public string Purpose { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }

    public DateTime? EmailSentAtUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class AdminAdministratorDto
{
    public Guid Id { get; init; }

    public string Email { get; init; } = string.Empty;

    /// <summary><c>Active</c>, <c>Blocked</c> or <c>Invited</c>. UI hint only.</summary>
    public string Status { get; init; } = string.Empty;

    public bool IsEmailConfirmed { get; init; }

    public bool IsBlocked { get; init; }

    /// <summary>UI hint only; authorization is always enforced by the server.</summary>
    public bool IsCurrentUser { get; init; }

    /// <summary>UI hint only; the server re-checks the invariant on every mutation.</summary>
    public bool IsLastActiveAdmin { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public AdminInvitationDto? Invitation { get; init; }
}

public sealed class PagedAdminAdministratorsDto
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }

    public IReadOnlyList<AdminAdministratorDto> Items { get; init; } = [];
}

public sealed class InviteAdminAccountRequestDto
{
    public string Email { get; init; } = string.Empty;
}

public sealed class UpdateAdminEmailRequestDto
{
    public string Email { get; init; } = string.Empty;
}

public sealed class AdminAccountBlockRequestDto
{
    /// <summary>Optional reason (max 200). Sanitized and used only in the audit description.</summary>
    public string? Reason { get; init; }
}

public sealed class AdminAccountResendInvitationResponseDto
{
    public Guid InvitationId { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public bool EmailDeliveryPending { get; init; }

    /// <summary>
    /// True when the delivery job was accepted by the background queue. False exposes a pending
    /// link whose e-mail was not enqueued, so the client must alert instead of claiming success.
    /// </summary>
    public bool EmailQueued { get; init; }
}

public sealed class AdminAccountTokenRequestDto
{
    public string Token { get; init; } = string.Empty;
}

public sealed class AdminAccountAcceptRequestDto
{
    public string Token { get; init; } = string.Empty;

    public string NewPassword { get; init; } = string.Empty;

    public string ConfirmPassword { get; init; } = string.Empty;
}

public sealed class AdminInvitationValidationDto
{
    public bool Valid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Purpose { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmailMasked { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ExpiresAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
}

public sealed class AdminAccountSetupResultDto
{
    public bool Succeeded { get; init; }

    public string? Purpose { get; init; }

    public string? Message { get; init; }

    public string? Error { get; init; }

    public string? Code { get; init; }
}

public enum AdminAccountOperationStatus
{
    Succeeded,
    NotFound,
    EmailAlreadyInUse,
    SellerEmailAlreadyInUse,
    InvalidEmail,
    NotActive,
    AlreadyBlocked,
    NotBlocked,
    LastActiveAdmin,
    SelfActionNotAllowed,
    InvitationNotPending,
    InvalidOrExpiredToken,
    TokenAlreadyUsed,
    WeakPassword,
    Failed
}

/// <summary>Stable machine-readable codes returned to the administrative (and public) clients.</summary>
public static class AdminAccountErrorCodes
{
    public const string EmailAlreadyInUse = "EMAIL_ALREADY_IN_USE";
    public const string SellerEmailAlreadyInUse = "SELLER_EMAIL_ALREADY_IN_USE";
    public const string InvalidEmail = "INVALID_EMAIL";
    public const string AdminNotFound = "ADMIN_NOT_FOUND";
    public const string AdminNotActive = "ADMIN_NOT_ACTIVE";
    public const string AdminAlreadyBlocked = "ADMIN_ALREADY_BLOCKED";
    public const string AdminNotBlocked = "ADMIN_NOT_BLOCKED";
    public const string LastActiveAdmin = "LAST_ACTIVE_ADMIN";
    public const string SelfActionNotAllowed = "SELF_ACTION_NOT_ALLOWED";
    public const string InvitationNotPending = "INVITATION_NOT_PENDING";
    public const string InvalidOrExpiredToken = "INVALID_OR_EXPIRED_TOKEN";
    public const string TokenAlreadyUsed = "TOKEN_ALREADY_USED";
    public const string WeakPassword = "WEAK_PASSWORD";
    public const string AdminBlocked = "ADMIN_BLOCKED";

    public static string ForStatus(AdminAccountOperationStatus status) => status switch
    {
        AdminAccountOperationStatus.EmailAlreadyInUse => EmailAlreadyInUse,
        AdminAccountOperationStatus.SellerEmailAlreadyInUse => SellerEmailAlreadyInUse,
        AdminAccountOperationStatus.InvalidEmail => InvalidEmail,
        AdminAccountOperationStatus.NotFound => AdminNotFound,
        AdminAccountOperationStatus.NotActive => AdminNotActive,
        AdminAccountOperationStatus.AlreadyBlocked => AdminAlreadyBlocked,
        AdminAccountOperationStatus.NotBlocked => AdminNotBlocked,
        AdminAccountOperationStatus.LastActiveAdmin => LastActiveAdmin,
        AdminAccountOperationStatus.SelfActionNotAllowed => SelfActionNotAllowed,
        AdminAccountOperationStatus.InvitationNotPending => InvitationNotPending,
        AdminAccountOperationStatus.InvalidOrExpiredToken => InvalidOrExpiredToken,
        AdminAccountOperationStatus.TokenAlreadyUsed => TokenAlreadyUsed,
        AdminAccountOperationStatus.WeakPassword => WeakPassword,
        _ => "ADMIN_ACCOUNT_OPERATION_FAILED"
    };
}

public sealed class AdminAccountOperationResult
{
    public AdminAccountOperationStatus Status { get; init; }

    public AdminAdministratorDto? Administrator { get; init; }

    public AdminInvitationDto? Invitation { get; init; }

    /// <summary>Sanitized message only; never contains a token, hash or password.</summary>
    public string? Error { get; init; }

    public string? Code { get; init; }

    /// <summary>
    /// True when the delivery job for <see cref="Invitation"/> was handed to the background
    /// queue. False means the committed invitation is still pending and must be resent by an
    /// operator; the operation itself is not rolled back.
    /// </summary>
    public bool EmailQueued { get; init; }

    public bool Succeeded => Status == AdminAccountOperationStatus.Succeeded;
}

public sealed class AdminAccountInvitationDeliveryResult
{
    public AdminAccountOperationStatus Status { get; init; }

    public Guid? InvitationId { get; init; }

    public DateTime? ExpiresAtUtc { get; init; }

    public bool EmailDeliveryPending { get; init; }

    /// <summary>
    /// True when the delivery job was accepted by the background queue. False exposes a pending
    /// invitation whose e-mail was not enqueued, so the caller can alert the operator.
    /// </summary>
    public bool EmailQueued { get; init; }

    public string? Error { get; init; }

    public string? Code { get; init; }

    public bool Succeeded => Status == AdminAccountOperationStatus.Succeeded;
}
