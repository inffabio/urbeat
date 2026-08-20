using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;
using Urbeat.PrintAgent.Storage;

namespace Urbeat.PrintAgent.Tests;

public class PrintJobServiceTests
{
    private sealed class FakePrintExecutor : ILocalPrintExecutor
    {
        private readonly PrintOutcome _outcome;
        private readonly string? _jobId;

        public FakePrintExecutor(PrintOutcome outcome, string? jobId = null)
        {
            _outcome = outcome;
            _jobId = jobId;
        }

        public Task<LocalPrintExecutionResult> PrintRawTextAsync(string printerName, string rawText, CancellationToken cancellationToken)
        {
            return Task.FromResult(new LocalPrintExecutionResult
            {
                Outcome = _outcome,
                JobId = _jobId,
                Message = $"{_outcome}:{printerName}"
            });
        }
    }

    private sealed class CountingPrintExecutor : ILocalPrintExecutor
    {
        public int Calls { get; private set; }

        public Task<LocalPrintExecutionResult> PrintRawTextAsync(string printerName, string rawText, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new LocalPrintExecutionResult
            {
                Outcome = PrintOutcome.Queued,
                JobId = "42",
                Message = "queued"
            });
        }
    }

    [Fact]
    public async Task BuildOrderJobAsync_uses_pos_58_without_auto_cut_by_default()
    {
        var service = new PrintJobService(new LocalPrinterDiscovery(), new FakePrintExecutor(PrintOutcome.Queued, "42"));
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
        job.Status.Should().Be("queued");
        job.JobId.Should().Be("42");
        job.Message.Should().Be("Queued:POS-58 Balcao");
    }

    [Theory]
    [InlineData(PrintOutcome.Queued, "queued")]
    [InlineData(PrintOutcome.Sent, "sent")]
    [InlineData(PrintOutcome.Failed, "failed")]
    [InlineData(PrintOutcome.Unknown, "unknown")]
    public async Task BuildTestJobAsync_maps_outcome_to_honest_status(PrintOutcome outcome, string expectedStatus)
    {
        var service = new PrintJobService(new LocalPrinterDiscovery(), new FakePrintExecutor(outcome));

        var job = await service.BuildTestJobAsync(new PrintTestRequest
        {
            PrinterName = "X",
            PrinterProfile = "pos-58"
        }, CancellationToken.None);

        job.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task BuildOrderJobAsync_records_job_in_store_with_honest_status()
    {
        var store = new PrintJobStore();
        var service = new PrintJobService(new LocalPrinterDiscovery(), new FakePrintExecutor(PrintOutcome.Failed, "7"), store);

        var job = await service.BuildOrderJobAsync(new PrintOrderRequest
        {
            PrinterName = "Broken",
            PrinterProfile = "pos-58",
            Order = new PrintOrderPayload { Code = "9" }
        }, CancellationToken.None);

        var records = store.List();

        records.Should().HaveCount(1);
        records[0].Status.Should().Be("failed");
        records[0].JobId.Should().Be("7");
        records[0].PrinterName.Should().Be("Broken");
        job.Status.Should().Be("failed");
    }

    [Fact]
    public async Task BuildOrderJobAsync_does_not_submit_same_order_twice()
    {
        var executor = new CountingPrintExecutor();
        var service = new PrintJobService(new LocalPrinterDiscovery(), executor, new PrintJobStore());
        var request = new PrintOrderRequest
        {
            OrderId = "order-1",
            PrinterName = "POS-58 Balcao",
            PrinterProfile = "pos-58",
            Order = new PrintOrderPayload { Code = "URB-12345678" }
        };

        var first = await service.BuildOrderJobAsync(request, CancellationToken.None);
        var second = await service.BuildOrderJobAsync(request, CancellationToken.None);

        executor.Calls.Should().Be(1);
        second.JobId.Should().Be(first.JobId);
        second.Status.Should().Be(first.Status);
    }
}
