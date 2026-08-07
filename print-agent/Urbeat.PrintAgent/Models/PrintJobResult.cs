namespace Urbeat.PrintAgent.Models;

public sealed class PrintJobResult
{
    public string Status { get; set; } = "simulated";

    public string Message { get; set; } = string.Empty;

    public string PrintedAtUtc { get; set; } = string.Empty;

    public string PrinterName { get; set; } = string.Empty;

    public string ProfileId { get; set; } = "pos-58";

    public string PaperWidth { get; set; } = "58mm";

    public bool AutoCut { get; set; }

    public string RawText { get; set; } = string.Empty;
}
