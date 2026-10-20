using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Storage;

public interface IPrintJobStore
{
    void Add(PrintJobRecord job);

    void Update(PrintJobRecord job);

    PrintJobRecord? FindByOperationKey(string operationKey);

    IReadOnlyList<PrintJobRecord> List();

    PrintJobRecord? Get(string id);
}
