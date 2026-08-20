using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Storage;

public sealed class PrintJobStore : IPrintJobStore
{
    private readonly object _gate = new();
    private readonly List<PrintJobRecord> _jobs = [];

    public void Add(PrintJobRecord job)
    {
        lock (_gate)
        {
            _jobs.Add(job);
        }
    }

    public IReadOnlyList<PrintJobRecord> List()
    {
        lock (_gate)
        {
            return _jobs.ToArray();
        }
    }

    public PrintJobRecord? FindByOperationKey(string operationKey)
    {
        if (string.IsNullOrWhiteSpace(operationKey)) return null;

        lock (_gate)
        {
            return _jobs.FirstOrDefault(job => string.Equals(job.OperationKey, operationKey, StringComparison.Ordinal));
        }
    }

    public PrintJobRecord? Get(string id)
    {
        lock (_gate)
        {
            return _jobs.FirstOrDefault(job => string.Equals(job.Id, id, StringComparison.Ordinal));
        }
    }
}
