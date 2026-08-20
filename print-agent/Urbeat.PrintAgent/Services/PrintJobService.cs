using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Storage;

namespace Urbeat.PrintAgent.Services;

public sealed class PrintJobService : IPrintJobService
{
    private readonly ILocalPrinterDiscovery _localPrinterDiscovery;
    private readonly ILocalPrintExecutor _localPrintExecutor;
    private readonly IPrintJobStore _printJobStore;

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

        return await BuildAndPrintJobAsync("test", profile, request.PrinterName, rawText, cancellationToken);
    }

    public async Task<PrintJobResult> BuildOrderJobAsync(PrintOrderRequest request, CancellationToken cancellationToken)
    {
        var profile = _localPrinterDiscovery.GetProfile(request.PrinterProfile);
        var operationKey = BuildOperationKey(request);
        var existing = _printJobStore.FindByOperationKey(operationKey);
        if (existing is not null)
        {
            return new PrintJobResult
            {
                PrinterName = existing.PrinterName,
                ProfileId = existing.ProfileId,
                PaperWidth = profile.PaperWidth,
                AutoCut = profile.SupportsAutoCut,
                Status = existing.Status,
                Message = existing.Message,
                PrintedAtUtc = existing.CreatedAtUtc,
                JobId = existing.JobId
            };
        }

        var rawText = $"PEDIDO {request.Order.Code}\nTOTAL {request.Order.Total:0.00}\nUTC {request.Order.CreatedAtUtc}";

        return await BuildAndPrintJobAsync("order", profile, request.PrinterName, rawText, cancellationToken, operationKey);
    }

    private async Task<PrintJobResult> BuildAndPrintJobAsync(string kind, AgentPrinterDescriptor profile, string printerName, string rawText, CancellationToken cancellationToken, string operationKey = "")
    {
        var printResult = await _localPrintExecutor.PrintRawTextAsync(printerName, rawText, cancellationToken);
        var status = ToStatus(printResult.Outcome);
        var now = DateTime.UtcNow.ToString("O");

        _printJobStore.Add(new PrintJobRecord
        {
            Status = status,
            JobId = printResult.JobId,
            PrinterName = printerName,
            ProfileId = profile.ProfileId,
            Kind = kind,
            OperationKey = operationKey,
            CreatedAtUtc = now,
            Message = printResult.Message
        });

        return new PrintJobResult
        {
            PrinterName = printerName,
            ProfileId = profile.ProfileId,
            PaperWidth = profile.PaperWidth,
            AutoCut = profile.SupportsAutoCut,
            RawText = rawText,
            Status = status,
            Message = printResult.Message,
            PrintedAtUtc = now,
            JobId = printResult.JobId
        };
    }

    private static string ToStatus(PrintOutcome outcome) => outcome switch
    {
        PrintOutcome.Queued => "queued",
        PrintOutcome.Sent => "sent",
        PrintOutcome.Failed => "failed",
        _ => "unknown"
    };

    private static string BuildOperationKey(PrintOrderRequest request) =>
        string.IsNullOrWhiteSpace(request.OrderId)
            ? string.Empty
            : $"order:{request.OrderId}:printer:{request.PrinterName}:profile:{request.PrinterProfile}";
}
