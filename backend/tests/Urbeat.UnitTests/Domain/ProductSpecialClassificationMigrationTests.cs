using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Enums;
using Urbeat.Infrastructure.Persistence.Migrations;

namespace Urbeat.UnitTests.Domain;

public sealed class ProductSpecialClassificationMigrationTests
{
    private readonly AddProductSpecialClassification _migration = new();

    private string UpSql => string.Join(
        "\n",
        _migration.UpOperations.OfType<SqlOperation>().Select(operation => operation.Sql));

    [Fact]
    public void Product_ShouldDefaultToNoSpecialClassification()
    {
        var product = new Product();

        product.SpecialClassification.Should().BeNull();
    }

    [Fact]
    public void SpecialClassification_ShouldAcceptEveryApprovedValue()
    {
        Enum.GetValues<ProductSpecialClassification>()
            .Should()
            .BeEquivalentTo(new[]
            {
                ProductSpecialClassification.Offers,
                ProductSpecialClassification.BestSeller,
                ProductSpecialClassification.New,
            });
    }

    [Fact]
    public void SpecialClassification_ShouldSerializeAsStableStringValues()
    {
        JsonSerializer.Serialize(ProductSpecialClassification.Offers).Should().Be("\"Offers\"");
        JsonSerializer.Serialize(ProductSpecialClassification.BestSeller).Should().Be("\"BestSeller\"");
        JsonSerializer.Serialize(ProductSpecialClassification.New).Should().Be("\"New\"");

        JsonSerializer.Deserialize<ProductSpecialClassification>("\"Offers\"")
            .Should().Be(ProductSpecialClassification.Offers);
        JsonSerializer.Deserialize<ProductSpecialClassification?>("\"BestSeller\"")
            .Should().Be(ProductSpecialClassification.BestSeller);
        JsonSerializer.Deserialize<ProductSpecialClassification?>("null")
            .Should().BeNull();
    }

    [Fact]
    public void Up_ShouldAddNullableSpecialClassificationColumn_ToProducts()
    {
        var column = _migration.UpOperations
            .OfType<AddColumnOperation>()
            .Single(x => x.Table == "Products" && x.Name == "SpecialClassification");

        column.IsNullable.Should().BeTrue();
        column.ClrType.Should().Be(typeof(string));
        column.MaxLength.Should().Be(20);
    }

    [Fact]
    public void Up_ShouldBackfillWithApprovedPrecedence()
    {
        UpSql.Should().Contain("UPDATE \"Products\"");
        UpSql.Should().Contain("SET \"SpecialClassification\" = CASE");
        UpSql.Should().Contain("WHEN \"IsFeatured\" THEN 'Offers'");
        UpSql.Should().Contain("WHEN \"IsBestSeller\" THEN 'BestSeller'");
        UpSql.Should().Contain("WHEN \"IsNew\" THEN 'New'");
        UpSql.Should().Contain("ELSE NULL");

        var featuredIndex = UpSql.IndexOf("WHEN \"IsFeatured\"", StringComparison.Ordinal);
        var bestSellerIndex = UpSql.IndexOf("WHEN \"IsBestSeller\"", StringComparison.Ordinal);
        var newIndex = UpSql.IndexOf("WHEN \"IsNew\"", StringComparison.Ordinal);

        featuredIndex.Should().BeLessThan(bestSellerIndex);
        bestSellerIndex.Should().BeLessThan(newIndex);
    }

    [Fact]
    public void Up_ShouldKeepLegacyClassificationColumnsForCompatibility()
    {
        var up = _migration.UpOperations;

        up.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Products" && x.Name == "IsFeatured");
        up.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Products" && x.Name == "IsBestSeller");
        up.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Products" && x.Name == "IsNew");
    }

    [Fact]
    public void Down_ShouldDropOnlyTheSpecialClassificationColumn()
    {
        var down = _migration.DownOperations;

        down.OfType<DropColumnOperation>()
            .Should().ContainSingle(x => x.Table == "Products" && x.Name == "SpecialClassification");

        down.OfType<DropColumnOperation>()
            .Should().NotContain(x => x.Table == "Products" && (x.Name == "IsFeatured" || x.Name == "IsBestSeller" || x.Name == "IsNew"));
    }
}
