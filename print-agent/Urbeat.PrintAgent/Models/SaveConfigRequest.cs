namespace Urbeat.PrintAgent.Models;

public sealed class SaveConfigRequest
{
    public string PreferredMode { get; set; } = "local-agent";

    public string PreferredProfile { get; set; } = "pos-58";

    public string PrinterName { get; set; } = string.Empty;

    public string PaperWidth { get; set; } = "58mm";

    public bool AutoCut { get; set; }

    public string LocalToken { get; set; } = string.Empty;
}
