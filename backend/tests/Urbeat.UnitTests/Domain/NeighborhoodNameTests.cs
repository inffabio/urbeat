using Urbeat.Domain.Geo;
using Xunit;

namespace Urbeat.UnitTests.Domain;

public sealed class NeighborhoodNameTests
{
    [Theory]
    [InlineData("  Bela Vista ", "bela vista")]
    [InlineData("BELA VISTA", "bela vista")]
    [InlineData("Bela  Vista", "bela vista")]
    [InlineData("Bela-Vista", "bela vista")]
    [InlineData("Bela/Vista", "bela vista")]
    [InlineData("Bela.Vista", "bela vista")]
    [InlineData("José Bonifácio", "jose bonifacio")]
    [InlineData("Açaí", "acai")]
    [InlineData("D'Ávila", "d avila")]
    [InlineData("Vila Sésamo (Setor 2)", "vila sesamo setor 2")]
    public void Normalize_produces_the_canonical_form(string input, string expected)
    {
        Assert.Equal(expected, NeighborhoodName.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Normalize_returns_empty_for_blank_input(string? value)
    {
        Assert.Equal(string.Empty, NeighborhoodName.Normalize(value));
    }

    [Fact]
    public void Normalize_matches_the_python_canonical_contract()
    {
        // The import pipeline computes the same value in Python. Any change here requires a
        // matching change in backend/scripts/import/cnefe_import.py and a backfill.
        const string source = "Alto da Boa Vista - Zona Oeste";

        Assert.Equal("alto da boa vista zona oeste", NeighborhoodName.Normalize(source));
    }

    [Theory]
    [InlineData("Rua", null, "das Flores", "Rua das Flores")]
    [InlineData("Avenida", null, "Paulista", "Avenida Paulista")]
    [InlineData(null, null, "  Rua   Sem  Tipo ", "Rua Sem Tipo")]
    [InlineData(" ", "", "Linha", "Linha")]
    public void Display_name_is_composed_without_inventing_parts(
        string? streetType,
        string? streetTitle,
        string streetName,
        string expected)
    {
        Assert.Equal(expected, NeighborhoodName.ComposeDisplayName(streetType, streetTitle, streetName));
    }

    [Fact]
    public void Zip_code_is_normalized_to_eight_digits_or_null()
    {
        Assert.Equal("01310100", NeighborhoodName.NormalizeZipCode("01310-100"));
        Assert.Equal("01310100", NeighborhoodName.NormalizeZipCode(" 01310 100 "));
        Assert.Null(NeighborhoodName.NormalizeZipCode("123"));
        Assert.Null(NeighborhoodName.NormalizeZipCode(""));
        Assert.Null(NeighborhoodName.NormalizeZipCode(null));
        Assert.Null(NeighborhoodName.NormalizeZipCode("0000-0000"));
    }

    [Fact]
    public void Address_point_hash_is_stable_and_ignores_insignificant_differences()
    {
        var first = NeighborhoodName.ComputeSourceRowHash("35", "3550308", "Bela Vista", "Avenida Paulista", "01310100", -23.5613991, -46.6558819);
        var second = NeighborhoodName.ComputeSourceRowHash("35", "3550308", "Bela Vista", "Avenida Paulista", "01310100", -23.5613991, -46.6558819);
        var different = NeighborhoodName.ComputeSourceRowHash("35", "3550308", "Bela Vista", "Avenida Paulista", "01310100", -23.5613992, -46.6558819);

        Assert.Equal(64, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void Source_row_hash_matches_the_python_importer()
    {
        // The importer (backend/scripts/import/cnefe_import.py) computes the same value for the
        // same tuple. A change in either implementation requires updating both tests.
        var hash = NeighborhoodName.ComputeSourceRowHash(
            "35",
            "3550308",
            "Bela Vista",
            "Avenida Paulista",
            "01310100",
            -23.5613991,
            -46.6558819);

        Assert.Equal("3fb876390ab24bb96bde463f583cbc4d00c68cc6c6d6c376bb3dd8f9be4662c5", hash);
    }
}
