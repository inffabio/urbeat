namespace Urbeat.Application.Interfaces;

public interface IOutboxDispatcher
{
    Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default);
}
