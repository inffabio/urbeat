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

    [Fact]
    public void GetRecommendedProfiles_includes_generic_connection_specific_profiles()
    {
        var service = new LocalPrinterDiscovery();

        var profiles = service.GetRecommendedProfiles();

        profiles.Select(p => p.ProfileId).Should().Contain(new[]
        {
            "generic-pos-58-usb",
            "generic-pos-58-bluetooth-android",
            "generic-pos-58-wifi",
            "generic-pos-80-wifi"
        });
    }

    [Fact]
    public void generic_pos_58_bluetooth_android_is_catalog_only_without_desktop_executor()
    {
        var service = new LocalPrinterDiscovery();

        var profile = service.GetProfile("generic-pos-58-bluetooth-android");

        profile.CatalogOnly.Should().BeTrue();
        profile.Connection.Should().Be("bluetooth");
        profile.PaperWidth.Should().Be("58mm");
    }

    [Fact]
    public void epson_profiles_are_marked_experimental_when_sku_validation_is_missing()
    {
        var service = new LocalPrinterDiscovery();

        var m30 = service.GetProfile("epson-tm-m30iii");
        m30.Experimental.Should().BeTrue();
        m30.PaperWidth.Should().Be("80mm");
        m30.Connection.Should().Be("wifi|ethernet");

        var t20 = service.GetProfile("epson-tm-t20iii");
        t20.Experimental.Should().BeTrue();
        t20.PaperWidth.Should().Be("80mm");
        t20.Connection.Should().Be("usb|ethernet");
    }

    [Fact]
    public void every_profile_exposes_connection_protocol_and_capabilities()
    {
        var service = new LocalPrinterDiscovery();

        var profiles = service.GetRecommendedProfiles();

        profiles.Should().NotBeEmpty();
        profiles.Should().OnlyContain(p =>
            !string.IsNullOrWhiteSpace(p.Connection) &&
            !string.IsNullOrWhiteSpace(p.Protocol) &&
            p.Capabilities != null);
    }

    [Fact]
    public void ParseLpStatPrinters_extracts_name_before_accepting_requests()
    {
        var output = "Minha Impressora aceitando solicitações desde sex 05 ago 2026 12:00:00\n";

        var names = LocalPrinterDiscovery.ParseLpStatPrinters(output);

        names.Should().Equal(new[] { "Minha Impressora" });
    }

    [Fact]
    public void ParseLpStatPrinters_handles_not_accepting_and_english_markers()
    {
        var output = "BackOffice accepting requests since Fri 05 Aug 2026\n" +
                     "Cozinha não está aceitando solicitações desde qua 03 ago 2026\n";

        var names = LocalPrinterDiscovery.ParseLpStatPrinters(output);

        names.Should().Equal(new[] { "BackOffice", "Cozinha" });
    }

    [Fact]
    public void ParseLpStatPrinters_falls_back_to_first_token_and_dedupes()
    {
        var output = "RawName\nRawName\n\n";

        var names = LocalPrinterDiscovery.ParseLpStatPrinters(output);

        names.Should().Equal(new[] { "RawName" });
    }

    [Fact]
    public void ExtractLpRequestId_reads_request_id_only_when_cups_reports_one()
    {
        LocalPrintExecutor.ExtractLpRequestId("request id is Cozinha-42 (1 file(s))").Should().Be("Cozinha-42");
        LocalPrintExecutor.ExtractLpRequestId("no request id here").Should().BeNull();
    }
}
