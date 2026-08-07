namespace Urbeat.Application.Interfaces;

public interface IEfUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
