namespace Urbeat.Application.DTOs;

public sealed class ReorderStoreCategoriesResult
{
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public bool Invalid { get; init; }
}
