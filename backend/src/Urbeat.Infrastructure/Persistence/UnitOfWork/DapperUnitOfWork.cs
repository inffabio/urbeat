using System.Data;
using Urbeat.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Persistence.UnitOfWork;

public sealed class DapperUnitOfWork : IDapperUnitOfWork
{
    private readonly ApplicationDbContext _dbContext;

    public DapperUnitOfWork(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<IDbConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Dapper unit of work requires a relational provider.");
        }

        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await operation(connection, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
