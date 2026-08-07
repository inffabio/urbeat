using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Services;

public sealed class PrintJobService : IPrintJobService
{
    private readonly ILocalPrinterDiscovery _localPrinterDiscovery;
    private readonly ILocalPrintExecutor _localPrintExecutor;

    public PrintJobService()
        : this(new LocalPrinterDiscovery(), new LocalPrintExecutor())
    {
    }

    public PrintJobService(ILocalPrinterDiscovery localPrinterDiscovery, ILocalPrintExecutor localPrintExecutor)
    {
        _localPrinterDiscovery = localPrinterDiscovery;
        _localPrintExecutor = localPrintExecutor;
    }

    public async Task<PrintJobResult> BuildTestJobAsync(PrintTestRequest request, CancellationToken cancellationToken)
    {
        var profile = _localPrinterDiscovery.GetProfile(request.PrinterProfile);
        var rawText = $"URBEAT TESTE\nPERFIL {profile.ProfileId}\n{request.Message}";

        return await BuildAndPrintJobAsync(profile, request.PrinterName, rawText, cancellationToken);
    }

    public async Task<PrintJobResult> BuildOrderJobAsync(PrintOrderRequest request, CancellationToken cancellationToken)
    {
        var profile = _localPrinterDiscovery.GetProfile(request.PrinterProfile);
        var rawText = $"PEDIDO {request.Order.Code}\nTOTAL {request.Order.Total:0.00}\nUTC {request.Order.CreatedAtUtc}";

        return await BuildAndPrintJobAsync(profile, request.PrinterName, rawText, cancellationToken);
    }

    private async Task<PrintJobResult> BuildAndPrintJobAsync(AgentPrinterDescriptor profile, string printerName, string rawText, CancellationToken cancellationToken)
    {
        var printResult = await _localPrintExecutor.PrintRawTextAsync(printerName, rawText, cancellationToken);

        return new PrintJobResult
        {
            PrinterName = printerName,
            ProfileId = profile.ProfileId,
            PaperWidth = profile.PaperWidth,
            AutoCut = profile.SupportsAutoCut,
            RawText = rawText,
            Status = printResult.Success ? "printed" : "failed",
            Message = printResult.Message,
            PrintedAtUtc = DateTime.UtcNow.ToString("O")
        };
    }
}
