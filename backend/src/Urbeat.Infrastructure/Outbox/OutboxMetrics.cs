using System.Diagnostics.Metrics;

namespace Urbeat.Infrastructure.Outbox;

public static class OutboxMetrics
{
    public const string MeterName = "Urbeat.Outbox";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<int> ProcessedTotal = Meter.CreateCounter<int>(
        "urbeat_outbox_processed_total",
        "messages",
        "Total de mensagens processadas com sucesso");

    private static readonly Counter<int> FailedTotal = Meter.CreateCounter<int>(
        "urbeat_outbox_failed_total",
        "messages",
        "Total de mensagens que entraram em falha terminal");

    private static readonly Counter<int> RetriedTotal = Meter.CreateCounter<int>(
        "urbeat_outbox_retried_total",
        "messages",
        "Total de reenvios agendados (falhas transientes)");

    private static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "urbeat_outbox_processing_duration_seconds",
        "s",
        "Duração do processamento de uma mensagem");

    public static void RecordProcessed() => ProcessedTotal.Add(1);

    public static void RecordFailed() => FailedTotal.Add(1);

    public static void RecordRetried() => RetriedTotal.Add(1);

    public static void RecordProcessingDuration(double seconds) => ProcessingDuration.Record(seconds);
}
