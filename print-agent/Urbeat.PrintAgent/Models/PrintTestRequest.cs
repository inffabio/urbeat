namespace Urbeat.PrintAgent.Models;

public sealed class PrintTestRequest
{
    public string PrinterName { get; set; } = string.Empty;

    public string PrinterProfile { get; set; } = "pos-58";

    public string PaperWidth { get; set; } = "58mm";

    public bool AutoCut { get; set; }

    public string Message { get; set; } = "Teste de impressao Urbeat";
}
