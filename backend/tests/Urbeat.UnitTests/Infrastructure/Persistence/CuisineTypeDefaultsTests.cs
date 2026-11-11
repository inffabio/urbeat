using System.Globalization;
using FluentAssertions;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class CuisineTypeDefaultsTests
{
    private static readonly string[] ExpectedDefaults =
    [
        "Açaíteria",
        "Batata Recheada",
        "Cachorro-quente (Hot Dog)",
        "Cafeteria",
        "Caldos e Sopas",
        "Comida Japonesa",
        "Comida Saudável (Fitness)",
        "Confeitaria",
        "Creperia",
        "Doceria",
        "Espetinhos e Churrasco",
        "Hamburgueria",
        "Marmitaria",
        "Massas e Lasanhas",
        "Padaria",
        "Pastelaria",
        "Pizzaria",
        "Salgaderia",
        "Sorveteria",
        "Tapiocaria"
    ];

    [Fact]
    public void Names_ShouldContainExactlyTheTwentyProtectedDefaults()
    {
        CuisineTypeDefaults.Names.Should().Equal(ExpectedDefaults);
    }

    [Fact]
    public void Names_ShouldBeAlphabeticallySortedWithPtBrCollation()
    {
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

        CuisineTypeDefaults.Names.Should().Equal(ExpectedDefaults.OrderBy(name => name, comparer));
    }

    [Fact]
    public void Names_ShouldNotContainNumberingOrLegacyDefaults()
    {
        CuisineTypeDefaults.Names.Should().OnlyContain(name => !char.IsDigit(name[0]));

        CuisineTypeDefaults.Names.Should().NotContain([
            "Açaiteria",
            "Churrascaria",
            "Comida Árabe",
            "Comida Mexicana",
            "Lanches",
            "Sucos e Vitaminas"
        ]);
    }

    [Theory]
    [InlineData("Pizzaria", true)]
    [InlineData("pizzaria", true)]
    [InlineData(" Pizzaria ", true)]
    [InlineData("Açaíteria", true)]
    [InlineData("açaíteria", true)]
    [InlineData("CACHORRO-QUENTE (HOT DOG)", true)]
    [InlineData("Comida Saudável (Fitness)", true)]
    [InlineData("Comida Baiana", false)]
    [InlineData("Acaiteria", false)]
    [InlineData("Lanches", false)]
    [InlineData("", false)]
    public void IsDefaultName_ShouldMatchOnlyCanonicalProtectedNames(string name, bool expected)
    {
        CuisineTypeDefaults.IsDefaultName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("acaiteria", true)]
    [InlineData("cachorro-quente (hot dog)", true)]
    [InlineData("comida saudavel (fitness)", true)]
    [InlineData("tapiocaria", true)]
    [InlineData("acaiiteria", false)]
    [InlineData("lanches", false)]
    [InlineData("comida arabe", false)]
    [InlineData("", false)]
    public void IsDefaultNormalizedName_ShouldMatchOnlyNormalizedProtectedNames(string name, bool expected)
    {
        CuisineTypeDefaults.IsDefaultNormalizedName(name).Should().Be(expected);
    }
}
