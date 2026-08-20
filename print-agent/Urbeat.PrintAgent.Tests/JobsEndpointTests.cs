using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

public class JobsEndpointTests
{
    [Fact]
    public async Task Post_print_test_then_get_jobs_returns_recorded_job_with_queued_status()
    {
        await using var fixture = new PrintAgentApiFactory();
        var client = fixture.CreateClient();

        var testResponse = await client.PostAsJsonAsync("/print/test", new PrintTestRequest
        {
            PrinterName = "POS-58 Balcao",
            PrinterProfile = "pos-58",
            Message = "hello"
        });

        testResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var job = await testResponse.Content.ReadFromJsonAsync<PrintJobResult>();
        job.Should().NotBeNull();
        job!.Status.Should().Be("queued");
        job.JobId.Should().Be("42");

        var jobsResponse = await client.GetAsync("/jobs");
        jobsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var jobs = await jobsResponse.Content.ReadFromJsonAsync<List<PrintJobRecord>>();
        jobs.Should().HaveCount(1);
        jobs![0].Status.Should().Be("queued");
        jobs[0].JobId.Should().Be("42");
        jobs[0].PrinterName.Should().Be("POS-58 Balcao");
    }

    [Fact]
    public async Task Get_jobs_by_id_returns_record_or_not_found()
    {
        await using var fixture = new PrintAgentApiFactory();
        var client = fixture.CreateClient();

        await client.PostAsJsonAsync("/print/test", new PrintTestRequest
        {
            PrinterName = "X",
            PrinterProfile = "pos-58"
        });

        var list = await client.GetFromJsonAsync<List<PrintJobRecord>>("/jobs");
        var id = list![0].Id;

        var found = await client.GetAsync($"/jobs/{id}");
        found.StatusCode.Should().Be(HttpStatusCode.OK);

        var missing = await client.GetAsync("/jobs/does-not-exist");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class PrintAgentApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"urbeat-print-agent-{Guid.NewGuid():N}.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Agent:ConfigPath"] = _configPath
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILocalPrintExecutor, FakePrintExecutor>();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }
    }

    private sealed class FakePrintExecutor : ILocalPrintExecutor
    {
        public Task<LocalPrintExecutionResult> PrintRawTextAsync(string printerName, string rawText, CancellationToken cancellationToken)
        {
            return Task.FromResult(new LocalPrintExecutionResult
            {
                Outcome = PrintOutcome.Queued,
                JobId = "42",
                Message = "queued"
            });
        }
    }
}
