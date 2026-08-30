namespace Urbeat.Application.Outbox;

public sealed class OutboxProcessingResult
{
    public bool Success { get; private init; }

    public bool Retryable { get; private init; }

    public string? Error { get; private init; }

    public static OutboxProcessingResult Ok() => new() { Success = true };

    public static OutboxProcessingResult Retry(string error) => new() { Success = false, Retryable = true, Error = error };

    public static OutboxProcessingResult Fail(string error) => new() { Success = false, Retryable = false, Error = error };
}
