namespace Urbeat.PrintAgent.Models;

public sealed class PrintOrderRequest
{
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Stable dashboard-generated idempotency key (order + acceptance + printer/profile). It is
    /// validated and used only as a key; it never contains a command or a file path.
    /// </summary>
    public string OperationKey { get; set; } = string.Empty;

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
