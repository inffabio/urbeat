namespace Urbeat.Application.Interfaces;

public interface IShortIdService
{
    Task<string> EncodeAsync(Guid entityId, CancellationToken cancellationToken = default);

    Task<Guid?> DecodeAsync(string code, CancellationToken cancellationToken = default);
}
