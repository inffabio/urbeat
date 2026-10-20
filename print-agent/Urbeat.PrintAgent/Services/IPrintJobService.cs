using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Services;

public interface IPrintJobService
{
    Task<PrintJobResult> BuildTestJobAsync(PrintTestRequest request, CancellationToken cancellationToken);

    Task<PrintJobResult> BuildOrderJobAsync(PrintOrderRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Explicit recovery for a failed or ambiguous job. Ambiguous jobs are never auto-resent because
    /// the agent cannot know whether the spooler accepted the bytes before a crash.
    /// </summary>
    Task<PrintJobResult?> RetryJobAsync(string jobId, CancellationToken cancellationToken);
}
