namespace Urbeat.Application.DTOs;

public sealed class PendingPrintJobsResponseDto
{
    public IReadOnlyCollection<PendingPrintJobDto> Items { get; set; } = Array.Empty<PendingPrintJobDto>();

    /// <summary>Server-generated cursor of the last returned item, for pagination within one scan.</summary>
    public long NextCursor { get; set; }

    public bool HasMore { get; set; }
}

public sealed class PendingPrintJobDto
{
    public long Cursor { get; set; }

    public Guid OrderId { get; set; }

    public Guid StoreId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public int AcceptanceSequence { get; set; }

    public DateTime AcceptedAtUtc { get; set; }

    public string Status { get; set; } = "pending";

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}

public sealed class RecordPrintJobResultRequestDto
{
    public bool Success { get; set; }

    /// <summary>True when the agent cannot know whether the spooler accepted the bytes.</summary>
    public bool Ambiguous { get; set; }

    public string OperationKey { get; set; } = string.Empty;

    public string? Message { get; set; }
}

public sealed class RecordPrintJobResultResponseDto
{
    public bool NotFound { get; set; }

    public PendingPrintJobDto? Job { get; set; }
}
