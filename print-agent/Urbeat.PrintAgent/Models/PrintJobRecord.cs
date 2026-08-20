namespace Urbeat.PrintAgent.Models;

public sealed class PrintJobRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Status { get; set; } = "unknown";

    public string? JobId { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public string ProfileId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string OperationKey { get; set; } = string.Empty;

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
