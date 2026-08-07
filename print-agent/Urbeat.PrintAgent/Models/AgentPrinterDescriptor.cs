namespace Urbeat.PrintAgent.Models;

public sealed class AgentPrinterDescriptor
{
    public string ProfileId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PaperWidth { get; set; } = "58mm";

    public bool SupportsAutoCut { get; set; }

    public string PreferredConnection { get; set; } = string.Empty;
}
