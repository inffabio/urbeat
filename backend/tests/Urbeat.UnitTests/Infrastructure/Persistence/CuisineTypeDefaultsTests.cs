using FluentAssertions;
using Urbeat.Infrastructure.Persistence;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class CuisineTypeDefaultsTests
{
    [Fact]
    public void Names_ShouldContainExactlyTheFifteenProtectedDefaults()
    {
        CuisineTypeDefaults.Names.Should().Equal(
            "Açaiteria",
            "Cafeteria",
            "Churrascaria",
            "Comida Árabe",
            "Comida Japonesa",
            "Comida Mexicana",
            "Doceria",
            "Hamburgueria",
            "Lanches",
            "Marmitaria",
            "Padaria",
            "Pastelaria",
            "Pizzaria",
            "Sucos e Vitaminas",
            "Tapiocaria");
    }

    [Theory]
    [InlineData("Pizzaria", true)]
    [InlineData("pizzaria", true)]
    [InlineData(" Pizzaria ", true)]
    [InlineData("Comida Baiana", false)]
    [InlineData("Acaiteria", false)]
    [InlineData("", false)]
    public void IsDefaultName_ShouldMatchOnlyCanonicalProtectedNames(string name, bool expected)
    {
        CuisineTypeDefaults.IsDefaultName(name).Should().Be(expected);
    }
}
