using FluentAssertions;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

public class LocalPrinterDiscoveryTests
{
    [Fact]
    public void GetRecommendedProfiles_prioritizes_pos_58_first_and_marks_no_auto_cut()
    {
        var service = new LocalPrinterDiscovery();

        var profiles = service.GetRecommendedProfiles();

        profiles[0].ProfileId.Should().Be("pos-58");
        profiles[0].PaperWidth.Should().Be("58mm");
        profiles[0].SupportsAutoCut.Should().BeFalse();
    }
}
