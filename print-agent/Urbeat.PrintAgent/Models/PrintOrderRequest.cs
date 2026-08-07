namespace Urbeat.PrintAgent.Models;

public sealed class PrintOrderRequest
{
    public string PrinterName { get; set; } = string.Empty;

    public string PrinterProfile { get; set; } = "pos-58";

    public string PaperWidth { get; set; } = "58mm";

    public bool AutoCut { get; set; }

    public PrintOrderPayload Order { get; set; } = new();
}

public sealed class PrintOrderPayload
{
    public string Code { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public string CreatedAtUtc { get; set; } = string.Empty;
}
