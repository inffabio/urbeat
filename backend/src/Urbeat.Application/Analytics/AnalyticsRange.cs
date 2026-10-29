namespace Urbeat.Application.Analytics;

public sealed record AnalyticsRange(
    AnalyticsGranularity Period,
    DateOnly AnchorDate,
    int? WeekBlock,
    DateTime StartUtc,
    DateTime EndUtcExclusive,
    int ChartYear);

public sealed class AnalyticsRangeResolution
{
    private AnalyticsRangeResolution(AnalyticsRange? range, IReadOnlyDictionary<string, string[]> errors)
    {
        Range = range;
        Errors = errors;
    }

    public AnalyticsRange? Range { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public bool IsValid => Range is not null && Errors.Count == 0;

    public static AnalyticsRangeResolution Success(AnalyticsRange range)
        => new(range, new Dictionary<string, string[]>());

    public static AnalyticsRangeResolution Failure(IReadOnlyDictionary<string, string[]> errors)
        => new(null, errors);
}
