using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Urbeat.PrintAgent.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Get_health_returns_ok_with_loopback_health_payload()
    {
        await using var fixture = new PrintAgentApiFactory();
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("ok");
        body.Mode.Should().Be("local-agent");
        body.BoundAddress.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task Post_config_persists_pos_58_settings_and_get_config_reads_them_back()
    {
        await using var fixture = new PrintAgentApiFactory();
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/config", new SaveConfigRequest
        {
            PreferredMode = "local-agent",
            PreferredProfile = "pos-58",
            PrinterName = "POS-58 Balcao",
            PaperWidth = "80mm",
            AutoCut = true,
            LocalToken = "token-1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.Content.ReadFromJsonAsync<AgentPrinterConfig>();
        saved.Should().NotBeNull();
        saved!.PreferredProfile.Should().Be("pos-58");
        saved.PaperWidth.Should().Be("58mm");
        saved.AutoCut.Should().BeFalse();

        var getResponse = await client.GetAsync("/config");
        var loaded = await getResponse.Content.ReadFromJsonAsync<AgentPrinterConfig>();

        loaded.Should().NotBeNull();
        loaded!.PrinterName.Should().Be("POS-58 Balcao");
        loaded.PaperWidth.Should().Be("58mm");
        loaded.AutoCut.Should().BeFalse();
    }

    [Fact]
    public async Task Get_printers_returns_recommended_profiles_with_pos_58_first()
    {
        await using var fixture = new PrintAgentApiFactory();
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/printers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PrinterCatalogResponse>();
        body.Should().NotBeNull();
        body!.RecommendedProfiles.Count.Should().BeGreaterThanOrEqualTo(2);
        body.RecommendedProfiles[0].ProfileId.Should().Be("pos-58");
        body.RecommendedProfiles[0].PaperWidth.Should().Be("58mm");
        body.RecommendedProfiles[0].SupportsAutoCut.Should().BeFalse();
    }

    [Fact]
    public async Task Post_print_endpoints_return_jobs_with_expected_profile_defaults()
    {
        await using var fixture = new PrintAgentApiFactory();
        var client = fixture.CreateClient();

        var testResponse = await client.PostAsJsonAsync("/print/test", new PrintTestRequest
        {
            PrinterName = "POS-58 Balcao",
            PrinterProfile = "pos-58",
            PaperWidth = "58mm",
            AutoCut = true
        });

        testResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var testJob = await testResponse.Content.ReadFromJsonAsync<PrintJobResult>();
        testJob.Should().NotBeNull();
        testJob!.ProfileId.Should().Be("pos-58");
        testJob.AutoCut.Should().BeFalse();

        var orderResponse = await client.PostAsJsonAsync("/print/order", new PrintOrderRequest
        {
            PrinterProfile = "pos-58",
            PaperWidth = "58mm",
            AutoCut = false,
            Order = new PrintOrderPayload
            {
                Code = "1024",
                Total = 25m,
                CreatedAtUtc = "2026-08-04T12:00:00Z"
            }
        });

        orderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var orderJob = await orderResponse.Content.ReadFromJsonAsync<PrintJobResult>();
        orderJob.Should().NotBeNull();
        orderJob!.ProfileId.Should().Be("pos-58");
        orderJob.AutoCut.Should().BeFalse();
        orderJob.RawText.Should().Contain("1024");
    }

    private sealed class PrintAgentApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"urbeat-print-agent-{Guid.NewGuid():N}.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(_ => { });
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Agent:ConfigPath"] = _configPath
                });
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
}
