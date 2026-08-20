namespace Urbeat.PrintAgent.Services;

public sealed class LocalPrintExecutionResult
{
    public PrintOutcome Outcome { get; set; } = PrintOutcome.Unknown;

    public string Message { get; set; } = string.Empty;

    public string? JobId { get; set; }
}
