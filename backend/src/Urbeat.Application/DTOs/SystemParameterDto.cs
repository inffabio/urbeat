namespace Urbeat.Application.DTOs;

/// <summary>
/// Admin-facing system parameter projection. <see cref="Value"/> is masked for
/// secret parameters; all other values are the canonical stored representation.
/// <para>
/// The platform business settings screen receives this shape only for the keys
/// in <c>BusinessSystemParameterCatalog</c>. Technical, infrastructure,
/// security and integration parameters keep the same shape but are never
/// returned by the business-scoped endpoints.
/// </para>
/// </summary>
public sealed record SystemParameterDto(
    Guid Id,
    string Key,
    string Value,
    string Type,
    string? Group,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    int MaxLength,
    string Display,
    string? Unit,
    decimal? MinValue,
    decimal? MaxValue,
    bool IsReadOnly,
    bool RequiresConfirmation,
    bool IsSecret
);
