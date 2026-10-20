using System.Collections.Concurrent;
using System.Text;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Storage;

namespace Urbeat.PrintAgent.Services;

public sealed class PrintJobService : IPrintJobService
{
    private const int MaxOperationKeyLength = 320;

    private readonly ILocalPrinterDiscovery _localPrinterDiscovery;
    private readonly ILocalPrintExecutor _localPrintExecutor;
    private readonly IPrintJobStore _printJobStore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks = new(StringComparer.Ordinal);

    public PrintJobService()
        : this(new LocalPrinterDiscovery(), new LocalPrintExecutor(), new PrintJobStore())
    {
    }

    public PrintJobService(ILocalPrinterDiscovery localPrinterDiscovery, ILocalPrintExecutor localPrintExecutor)
        : this(localPrinterDiscovery, localPrintExecutor, new PrintJobStore())
    {
    }

    public PrintJobService(ILocalPrinterDiscovery localPrinterDiscovery, ILocalPrintExecutor localPrintExecutor, IPrintJobStore printJobStore)
    {
        _localPrinterDiscovery = localPrinterDiscovery;
        _localPrintExecutor = localPrintExecutor;
        _printJobStore = printJobStore;
    }

    public async Task<PrintJobResult> BuildTestJobAsync(PrintTestRequest request, CancellationToken cancellationToken)
    {
        var profile = _localPrinterDiscovery.GetProfile(request.PrinterProfile);
        var rawText = $"URBEAT TESTE\nPERFIL {profile.ProfileId}\n{request.Message}";
        var printerError = await ValidatePrinterAsync(request.PrinterName, cancellationToken);
        if (printerError is not null)
        {
            return await RecordFailureAsync("test", profile, request.PrinterName, rawText, string.Empty, printerError, null);
        }

        return await BuildAndPrintJobAsync("test", profile, request.PrinterName, rawText, cancellationToken, string.Empty);
    }

    public async Task<PrintJobResult> BuildOrderJobAsync(PrintOrderRequest request, CancellationToken cancellationToken)
    {
        var profile = _localPrinterDiscovery.GetProfile(request.PrinterProfile);
        var operationKey = BuildOperationKey(request);

        if (string.IsNullOrEmpty(operationKey))
        {
            return await BuildOrderJobCoreAsync(request, profile, string.Empty, cancellationToken);
        }

        // Serialize check-then-print per operation key so two simultaneous at-least-once requests
        // cannot both submit the same acceptance.
        var gate = _operationLocks.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await BuildOrderJobCoreAsync(request, profile, operationKey, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PrintJobResult?> RetryJobAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = _printJobStore.Get(jobId);
        if (job is null) return null;

        if (!IsExplicitlyRetryable(job.Status))
        {
            throw new InvalidOperationException(
                $"Somente jobs failed ou ambiguous podem ser reenviados explicitamente. Status atual: {job.Status}.");
        }

        var profile = _localPrinterDiscovery.GetProfile(job.ProfileId);
        var printerError = await ValidatePrinterAsync(job.PrinterName, cancellationToken);
        if (printerError is not null)
        {
            return await RecordFailureAsync(job.Kind, profile, job.PrinterName, job.RawText, job.OperationKey, printerError, job);
        }

        return await BuildAndPrintJobAsync(
            job.Kind,
            profile,
            job.PrinterName,
            job.RawText,
            cancellationToken,
            job.OperationKey,
            job,
            allowAmbiguousRetry: true);
    }

    private async Task<PrintJobResult> BuildOrderJobCoreAsync(
        PrintOrderRequest request,
        AgentPrinterDescriptor profile,
        string operationKey,
        CancellationToken cancellationToken)
    {
        var existing = string.IsNullOrEmpty(operationKey)
            ? null
            : _printJobStore.FindByOperationKey(operationKey);

        if (existing is not null && (IsSubmitted(existing.Status) || IsAmbiguous(existing.Status)))
        {
            return BuildResult(existing, profile);
        }

        if (existing is not null && !IsExplicitlyRetryable(existing.Status) && IsInFlight(existing.Status))
        {
            return BuildResult(existing, profile);
        }

        var rawText = $"PEDIDO {request.Order.Code}\nTOTAL {request.Order.Total:0.00}\nUTC {request.Order.CreatedAtUtc}";
        var printerError = await ValidatePrinterAsync(request.PrinterName, cancellationToken);
        if (printerError is not null)
        {
            return await RecordFailureAsync("order", profile, request.PrinterName, rawText, operationKey, printerError, existing);
        }

        return await BuildAndPrintJobAsync("order", profile, request.PrinterName, rawText, cancellationToken, operationKey, existing);
    }

    private async Task<PrintJobResult> BuildAndPrintJobAsync(
        string kind,
        AgentPrinterDescriptor profile,
        string printerName,
        string rawText,
        CancellationToken cancellationToken,
        string operationKey,
        PrintJobRecord? existing = null,
        bool allowAmbiguousRetry = false)
    {
        if (existing is not null && IsAmbiguous(existing.Status) && !allowAmbiguousRetry)
        {
            return BuildResult(existing, profile);
        }

        var prepared = PrepareSendingRecord(kind, profile, printerName, rawText, operationKey, existing);

        LocalPrintExecutionResult printResult;
        try
        {
            printResult = await _localPrintExecutor.PrintRawTextAsync(printerName, rawText, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            prepared.Status = "ambiguous";
            prepared.Message = "O envio foi cancelado durante a impressão; o resultado físico é desconhecido.";
            Persist(prepared, existing);
            throw;
        }
        catch (Exception exception)
        {
            prepared.Status = "ambiguous";
            prepared.Message = $"Falha durante o envio; o resultado físico é desconhecido: {exception.Message}";
            Persist(prepared, existing);
            return ToResult(prepared, profile, rawText);
        }

        var status = ToStatus(printResult.Outcome);
        prepared.Status = status;
        prepared.JobId = printResult.JobId;
        prepared.Message = printResult.Message;
        Persist(prepared, existing);

        return ToResult(prepared, profile, rawText);
    }

    private PrintJobRecord PrepareSendingRecord(
        string kind,
        AgentPrinterDescriptor profile,
        string printerName,
        string rawText,
        string operationKey,
        PrintJobRecord? existing)
    {
        var record = existing ?? new PrintJobRecord();
        record.Status = "sending";
        record.PrinterName = printerName;
        record.ProfileId = profile.ProfileId;
        record.Kind = kind;
        record.OperationKey = operationKey;
        record.RawText = rawText;
        record.AttemptCount = existing is null ? 1 : existing.AttemptCount + 1;
        record.CreatedAtUtc = existing?.CreatedAtUtc is { Length: > 0 } createdAt
            ? createdAt
            : DateTime.UtcNow.ToString("O");
        record.Message = "Enviando para o spooler.";

        // Persist before the spooler call. A crash after this point is recovered as ambiguous and
        // never auto-resent, which is the only safe policy without physical printer feedback.
        Persist(record, existing);
        return record;
    }

    private void Persist(PrintJobRecord record, PrintJobRecord? existing)
    {
        if (existing is null)
        {
            _printJobStore.Add(record);
        }
        else
        {
            _printJobStore.Update(record);
        }
    }

    private Task<PrintJobResult> RecordFailureAsync(
        string kind,
        AgentPrinterDescriptor profile,
        string printerName,
        string rawText,
        string operationKey,
        string message,
        PrintJobRecord? existing)
    {
        var record = existing ?? new PrintJobRecord();
        record.Status = "failed";
        record.PrinterName = printerName;
        record.ProfileId = profile.ProfileId;
        record.Kind = kind;
        record.OperationKey = operationKey;
        if (!string.IsNullOrEmpty(rawText))
        {
            record.RawText = rawText;
        }
        record.AttemptCount = existing is null ? 1 : existing.AttemptCount + 1;
        record.CreatedAtUtc = existing?.CreatedAtUtc is { Length: > 0 } createdAt
            ? createdAt
            : DateTime.UtcNow.ToString("O");
        record.Message = message;

        Persist(record, existing);

        return Task.FromResult(ToResult(record, profile, record.RawText));
    }

    private async Task<string?> ValidatePrinterAsync(string printerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return "Nenhuma impressora local foi configurada.";
        }

        try
        {
            var installed = await _localPrinterDiscovery.ListInstalledPrintersAsync(cancellationToken);
            if (installed.Count == 0)
            {
                // Discovery may be unavailable (for example, no CUPS client). Let the platform
                // executor perform the authoritative check instead of rejecting a valid printer.
                return null;
            }

            return installed.Any(name => string.Equals(name, printerName.Trim(), StringComparison.OrdinalIgnoreCase))
                ? null
                : $"A impressora '{printerName}' não está instalada/configurada para o contexto do agente. "
                    + "No Windows Service (LocalSystem), instale a impressora para todos os usuários "
                    + "(machine-wide) ou use o modo console com o usuário da sessão.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Discovery failures are not proof that the configured printer is invalid.
            return null;
        }
    }

    private static bool IsSubmitted(string status) =>
        string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "sent", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguous(string status) =>
        string.Equals(status, "ambiguous", StringComparison.OrdinalIgnoreCase);

    private static bool IsInFlight(string status) =>
        string.Equals(status, "sending", StringComparison.OrdinalIgnoreCase);

    private static bool IsExplicitlyRetryable(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
        || IsAmbiguous(status);

    private static string BuildOperationKey(PrintOrderRequest request)
    {
        var provided = request.OperationKey?.Trim();
        if (!string.IsNullOrWhiteSpace(provided)
            && provided.Length <= MaxOperationKeyLength
            && provided.All(IsSafeKeyCharacter))
        {
            return provided;
        }

        if (string.IsNullOrWhiteSpace(request.OrderId))
        {
            return string.Empty;
        }

        return $"order:{Sanitize(request.OrderId)}:printer:{Sanitize(request.PrinterName)}:profile:{Sanitize(request.PrinterProfile)}";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(IsSafeKeyCharacter(character) ? character : '-');
        }

        var sanitized = builder.ToString().Trim('-', ':');
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized[..Math.Min(sanitized.Length, 80)];
    }

    private static bool IsSafeKeyCharacter(char character) =>
        char.IsLetterOrDigit(character)
        || character is ':' or '-' or '_' or '.' or '@';

    private static PrintJobResult BuildResult(PrintJobRecord record, AgentPrinterDescriptor profile) =>
        ToResult(record, profile, record.RawText);

    private static PrintJobResult ToResult(
        PrintJobRecord record,
        AgentPrinterDescriptor profile,
        string rawText) => new()
    {
        PrinterName = record.PrinterName,
        ProfileId = record.ProfileId,
        PaperWidth = profile.PaperWidth,
        AutoCut = profile.SupportsAutoCut,
        RawText = rawText,
        Status = record.Status,
        Message = record.Message,
        PrintedAtUtc = record.CreatedAtUtc,
        JobId = record.JobId,
        OperationKey = record.OperationKey
    };

    private static string ToStatus(PrintOutcome outcome) => outcome switch
    {
        PrintOutcome.Queued => "queued",
        PrintOutcome.Sent => "sent",
        PrintOutcome.Failed => "failed",
        _ => "unknown"
    };
}
