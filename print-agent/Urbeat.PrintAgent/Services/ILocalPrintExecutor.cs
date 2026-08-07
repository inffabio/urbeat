namespace Urbeat.PrintAgent.Services;

public interface ILocalPrintExecutor
{
    Task<LocalPrintExecutionResult> PrintRawTextAsync(string printerName, string rawText, CancellationToken cancellationToken);
}
