using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

public class PrintJobServiceTests
{
    private sealed class FakePrintExecutor : ILocalPrintExecutor
    {
        public Task<LocalPrintExecutionResult> PrintRawTextAsync(string printerName, string rawText, CancellationToken cancellationToken)
        {
            return Task.FromResult(new LocalPrintExecutionResult
            {
                Success = true,
                Message = $"printed:{printerName}"
            });
        }
    }

    [Fact]
    public async Task BuildOrderJobAsync_uses_pos_58_without_auto_cut_by_default()
    {
        var service = new PrintJobService(new LocalPrinterDiscovery(), new FakePrintExecutor());
        var request = new PrintOrderRequest
        {
            PrinterName = "POS-58 Balcao",
            PrinterProfile = "pos-58",
            PaperWidth = "58mm",
            AutoCut = false,
            Order = new PrintOrderPayload
            {
                Code = "1024",
                Total = 25m,
                CreatedAtUtc = "2026-08-04T12:00:00Z"
            }
        };

        var job = await service.BuildOrderJobAsync(request, CancellationToken.None);

        job.ProfileId.Should().Be("pos-58");
        job.AutoCut.Should().BeFalse();
        job.RawText.Should().Contain("1024");
        job.Status.Should().Be("printed");
        job.Message.Should().Be("printed:POS-58 Balcao");
    }
}
