using System.Data;

namespace Urbeat.Application.Interfaces;

public interface IDapperUnitOfWork
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<IDbConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
