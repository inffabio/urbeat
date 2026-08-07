using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Services;

public interface IPrintJobService
{
    Task<PrintJobResult> BuildTestJobAsync(PrintTestRequest request, CancellationToken cancellationToken);

    Task<PrintJobResult> BuildOrderJobAsync(PrintOrderRequest request, CancellationToken cancellationToken);
}
