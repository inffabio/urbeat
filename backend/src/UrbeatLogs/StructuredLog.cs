using System;

namespace UrbeatLogs;

public class StructuredLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Application { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? UserId { get; set; }
    public string? StoreId { get; set; }
    public string? OrderId { get; set; }
    public string? EventType { get; set; }
    public string? Exception { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? Provider { get; set; }
    public string? SourceContext { get; set; }
}