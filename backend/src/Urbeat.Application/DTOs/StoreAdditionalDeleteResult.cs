namespace Urbeat.Application.DTOs;

public sealed class StoreAdditionalDeleteResult
{
    public bool NotFound { get; init; }
    public bool Forbidden { get; init; }
    public bool HasProducts { get; init; }
}
