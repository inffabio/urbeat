namespace Urbeat.Application.DTOs;

public sealed record SystemParameterDto(
    string Key,
    string Value,
    string Type,
    string? Group,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);
