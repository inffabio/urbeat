namespace Urbeat.PrintAgent.Models;

public sealed class HealthResponse
{
    public string Status { get; set; } = "ok";

    public string Mode { get; set; } = "local-agent";

    public string BoundAddress { get; set; } = "127.0.0.1";
}
