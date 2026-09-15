using FluentAssertions;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.UnitTests.Infrastructure.Persistence;

public sealed class ProductOptionGroupTemplateTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public ProductOptionGroupTemplateTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-option-templates-{Guid.NewGuid()}")
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void Template_ShouldBelongToStore_WithCascadeDelete()
    {
        var entity = _db.Model.FindEntityType(typeof(ProductOptionGroupTemplate));

        entity.Should().NotBeNull();
        var storeFk = entity!.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Store));
        storeFk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void TemplateItem_ShouldCascadeDeleteWithTemplate()
    {
        var entity = _db.Model.FindEntityType(typeof(ProductOptionItemTemplate));

        entity.Should().NotBeNull();
        var groupFk = entity!.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(ProductOptionGroupTemplate));
        groupFk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Fact]
    public void Template_ShouldHaveNonUniqueIndex_OnStoreAndName()
    {
        var entity = _db.Model.FindEntityType(typeof(ProductOptionGroupTemplate));

        entity.Should().NotBeNull();
        var index = entity!.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ProductOptionGroupTemplate.StoreId), nameof(ProductOptionGroupTemplate.Name) }));

        index.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void ProductSnapshot_ShouldReferenceNullableTemplate_WithoutTouchingExistingGroups()
    {
        var entity = _db.Model.FindEntityType(typeof(ProductOptionGroup));

        entity.Should().NotBeNull();
        var templateProperty = entity!.FindProperty(nameof(ProductOptionGroup.TemplateId));
        templateProperty.Should().NotBeNull();
        templateProperty!.IsNullable.Should().BeTrue();

        var templateFk = entity.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(ProductOptionGroupTemplate));
        templateFk.DeleteBehavior.Should().Be(DeleteBehavior.SetNull);

        // O vínculo obrigatório com o produto permanece intacto.
        entity.GetForeignKeys().Should().Contain(fk =>
            fk.PrincipalEntityType.ClrType == typeof(Product) && fk.IsRequired);
    }
}
