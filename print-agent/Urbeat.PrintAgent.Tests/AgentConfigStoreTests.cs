using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Storage;

namespace Urbeat.PrintAgent.Tests;

public class AgentConfigStoreTests
{
    [Fact]
    public async Task SaveAsync_persists_and_reads_back_agent_config()
    {
        var tempFile = Path.GetTempFileName();

        try
        {
            var store = new AgentConfigStore(tempFile);
            var config = new AgentPrinterConfig
            {
                PreferredMode = "local-agent",
                PreferredProfile = "pos-58",
                PrinterName = "POS-58 Balcao",
                PaperWidth = "58mm",
                AutoCut = false,
                LocalToken = "secret-token"
            };

            await store.SaveAsync(config, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.PrinterName.Should().Be("POS-58 Balcao");
            loaded.AutoCut.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
