namespace Urbeat.Application.DTOs.Admin;

/// <summary>Query accepted by the administrative store listing.</summary>
public sealed class AdminStoresQueryDto
{
    /// <summary>Case-insensitive partial match on name, slug, owner e-mail and owner name.</summary>
    public string? Search { get; init; }

    /// <summary><c>all</c> (default), <c>onboarding</c>, <c>active</c> or <c>suspended</c>.</summary>
    public string? Status { get; init; }

    /// <summary><c>all</c> (default), <c>published</c> or <c>draft</c>.</summary>
    public string? Publication { get; init; }

    /// <summary><c>all</c> (default), <c>open</c> or <c>closed</c>.</summary>
    public string? OpenState { get; init; }

    /// <summary><c>all</c> (default), <c>active</c>, <c>overdue</c>, <c>blocked</c> or <c>none</c>.</summary>
    public string? Subscription { get; init; }

    /// <summary><c>name</c> (default), <c>name_desc</c>, <c>created</c>, <c>created_desc</c>, <c>updated</c>, <c>updated_desc</c>.</summary>
    public string? Sort { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed class AdminStoreOwnerDto
{
    public Guid UserId { get; init; }

    /// <summary>Null only when the owner identity no longer exists (opening the context fails).</summary>
    public string? Email { get; init; }

    /// <summary>FullName claim, falling back to the e-mail. Null when the identity is gone.</summary>
    public string? Name { get; init; }
}

public sealed class AdminStoreSubscriptionDto
{
    /// <summary><c>Active</c>, <c>Overdue</c>, <c>Blocked</c> or <c>None</c>.</summary>
    public string Status { get; init; } = "None";

    public string? PlanName { get; init; }

    public DateTime? NextDueDateUtc { get; init; }
}

public sealed class AdminStoreSummaryDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? LogoUrl { get; init; }

    public AdminStoreOwnerDto Owner { get; init; } = new();

    /// <summary><c>Onboarding</c>, <c>Active</c> or <c>Suspended</c>.</summary>
    public string Status { get; init; } = "Onboarding";

    public bool IsPublished { get; init; }

    public bool IsOpen { get; init; }

    public bool IsSubscriptionBlocked { get; init; }

    public AdminStoreSubscriptionDto Subscription { get; init; } = new();

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class PagedAdminStoresDto
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }

    public IReadOnlyCollection<AdminStoreSummaryDto> Items { get; init; } = [];
}

/// <summary>Optional body accepted when opening a context. Used only for auditing.</summary>
public sealed class AdminStoreAccessOpenRequestDto
{
    public string? Reason { get; init; }
}

/// <summary>One-time creation response. The raw token is never returned again.</summary>
public sealed class AdminStoreAccessContextDto
{
    public Guid ContextId { get; init; }

    public Guid AdminUserId { get; init; }

    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string StoreSlug { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>Opaque token. Delivered exactly once, on creation.</summary>
    public string Token { get; init; } = string.Empty;
}

/// <summary>Context metadata returned by <c>GET .../access-context</c>. Never contains the token.</summary>
public sealed class AdminStoreAccessContextMetadataDto
{
    public Guid ContextId { get; init; }

    public Guid AdminUserId { get; init; }

    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string StoreSlug { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
}

public enum AdminStoreAccessStatus
{
    Valid = 0,
    Invalid = 1,
    Expired = 2,
    AdminNotAllowed = 3,
    StoreMissing = 4,
    StoreMismatch = 5,
    StoreNotFound = 6,
    StoreOwnerNotFound = 7,
    AlreadyClosed = 8
}

public sealed record AdminStoreAccessValidation(
    AdminStoreAccessStatus Status,
    Guid? ContextId,
    Guid? AdminUserId,
    Guid? StoreId,
    Guid? StoreOwnerUserId,
    DateTime? ExpiresAtUtc,
    string? StoreName,
    string? StoreSlug)
{
    public bool IsValid => Status == AdminStoreAccessStatus.Valid;

    public static AdminStoreAccessValidation Failed(AdminStoreAccessStatus status)
        => new(status, null, null, null, null, null, null, null);
}

public sealed record AdminStoreAccessCreationResult(
    AdminStoreAccessStatus Status,
    AdminStoreAccessContextDto? Context)
{
    public static AdminStoreAccessCreationResult Failed(AdminStoreAccessStatus status)
        => new(status, null);
}
