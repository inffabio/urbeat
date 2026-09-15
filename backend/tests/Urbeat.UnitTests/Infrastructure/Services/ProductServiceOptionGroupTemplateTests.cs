using AutoMapper;
using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Application.Mappings;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Persistence.UnitOfWork;
using Urbeat.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class ProductServiceOptionGroupTemplateTests : IDisposable
{
    private readonly string _databaseName = $"urbeat-product-option-templates-{Guid.NewGuid()}";
    private readonly IMapper _mapper = new MapperConfiguration(cfg => cfg.AddProfile<EntityToDtoProfile>()).CreateMapper();
    private readonly ApplicationDbContext _db;
    private readonly ProductService _sut;

    public ProductServiceOptionGroupTemplateTests()
    {
        _db = CreateContext();
        _sut = CreateService(_db);
    }

    private ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options);

    private ProductService CreateService(ApplicationDbContext db) =>
        new(db, _mapper, new EfUnitOfWork(db), Mock.Of<IImageUploadService>());

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task ListOptionGroupTemplates_ShouldReturnOnlyOwnerStoreTemplates()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var otherStore = SeedStore(Guid.NewGuid(), "loja-b");
        _db.ProductOptionGroupTemplates.Add(BuildTemplate(store.Id, "Escolha um molho"));
        _db.ProductOptionGroupTemplates.Add(BuildTemplate(otherStore.Id, "Outra loja"));
        await _db.SaveChangesAsync();

        var result = await _sut.ListOptionGroupTemplatesAsync(owner, store.Id);

        result.Should().ContainSingle();
        result.Single().Name.Should().Be("Escolha um molho");
    }

    [Fact]
    public async Task ListOptionGroupTemplates_ShouldReturnEmpty_WhenNotOwner()
    {
        var store = SeedStore(Guid.NewGuid(), "loja-a");
        _db.ProductOptionGroupTemplates.Add(BuildTemplate(store.Id, "Escolha um molho"));
        await _db.SaveChangesAsync();

        var result = await _sut.ListOptionGroupTemplatesAsync(Guid.NewGuid(), store.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldCopySelectedTemplate_WithoutCreatingDuplicateTemplate()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var template = BuildTemplate(store.Id, "Escolha um molho",
            ("Molho 1", 5.00m), ("Molho 2", 5.50m));
        _db.ProductOptionGroupTemplates.Add(template);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Escolha um molho",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 2,
                    TemplateId = template.Id,
                    Items = template.Items.Select(i => new ProductOptionItemDto { Name = i.Name, Price = i.Price }).ToArray(),
                },
            },
        }, ipAddress: null);

        result.NotFound.Should().BeFalse();
        result.Product.Should().NotBeNull();
        var group = result.Product!.OptionGroups.Should().ContainSingle().Subject;
        group.TemplateId.Should().Be(template.Id);
        group.Name.Should().Be("Escolha um molho");
        group.Items.Should().HaveCount(2);
        group.Items.Select(i => i.Price).Should().BeEquivalentTo(new[] { 5.00m, 5.50m });

        (await _db.ProductOptionGroupTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateTemplate_ForNewlyAuthoredGroup()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Bordas",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 3,
                    Items = new[] { new ProductOptionItemDto { Name = "Catupiry", Price = 5m } },
                },
            },
        }, ipAddress: null);

        result.NotFound.Should().BeFalse();
        var group = result.Product!.OptionGroups.Should().ContainSingle().Subject;
        group.TemplateId.Should().NotBeNull();

        var templates = await _db.ProductOptionGroupTemplates.Include(t => t.Items).ToListAsync();
        templates.Should().ContainSingle();
        templates[0].Name.Should().Be("Bordas");
        templates[0].Items.Should().ContainSingle().Which.Name.Should().Be("Catupiry");
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectTemplateFromAnotherStore()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var otherStore = SeedStore(Guid.NewGuid(), "loja-b");
        var foreignTemplate = BuildTemplate(otherStore.Id, "Molho de outra loja");
        _db.ProductOptionGroupTemplates.Add(foreignTemplate);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Molho de outra loja",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 2,
                    TemplateId = foreignTemplate.Id,
                    Items = new[] { new ProductOptionItemDto { Name = "Item", Price = 1m } },
                },
            },
        }, ipAddress: null);

        result.NotFound.Should().BeTrue();
        (await _db.Products.CountAsync()).Should().Be(0);
        (await _db.ProductOptionGroupTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_ShouldKeepTemplateAndProductSnapshotIndependent()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        await _db.SaveChangesAsync();

        var created = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Bordas",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 3,
                    Items = new[] { new ProductOptionItemDto { Name = "Catupiry", Price = 5m } },
                },
            },
        }, ipAddress: null);

        var templateId = (await _db.ProductOptionGroupTemplates.SingleAsync()).Id;

        using var updateDb = CreateContext();
        var updateSut = CreateService(updateDb);
        var update = await updateSut.UpdateAsync(owner, created.Product!.Id, new UpdateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza editada",
            Description = "Desc",
            Price = 45m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Bordas premium",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 3,
                    TemplateId = templateId,
                    Items = new[] { new ProductOptionItemDto { Name = "Cheddar", Price = 7m } },
                },
            },
        }, ipAddress: null);

        update.NotFound.Should().BeFalse();
        var group = update.Product!.OptionGroups.Should().ContainSingle().Subject;
        group.Name.Should().Be("Bordas");
        group.TemplateId.Should().Be(templateId);
        group.Items.Should().ContainSingle().Which.Name.Should().Be("Catupiry");
        group.Items.Single().Price.Should().Be(5m);

        // O template permanece intacto.
        var template = await _db.ProductOptionGroupTemplates.Include(t => t.Items).SingleAsync();
        template.Name.Should().Be("Bordas");
        template.Items.Should().ContainSingle().Which.Name.Should().Be("Catupiry");
        (await _db.ProductOptionGroupTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAsync_ShouldNotDeleteTemplate_WhenGroupIsDeselected()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var template = BuildTemplate(store.Id, "Escolha um molho", ("Molho 1", 5m));
        _db.ProductOptionGroupTemplates.Add(template);
        await _db.SaveChangesAsync();

        var created = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Escolha um molho",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 2,
                    TemplateId = template.Id,
                    Items = new[] { new ProductOptionItemDto { Name = "Molho 1", Price = 5m } },
                },
            },
        }, ipAddress: null);

        var update = await _sut.UpdateAsync(owner, created.Product!.Id, new UpdateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = Array.Empty<ProductOptionGroupDto>(),
        }, ipAddress: null);

        update.NotFound.Should().BeFalse();
        update.Product!.OptionGroups.Should().BeEmpty();
        (await _db.ProductOptionGroupTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ShouldCopyTemplateValues_WhenTemplateSelected()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var template = BuildTemplate(store.Id, "Escolha um molho",
            ("Molho 1", 5.00m), ("Molho 2", 5.50m));
        _db.ProductOptionGroupTemplates.Add(template);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Nome editado",
                    ChoiceType = "single",
                    MinChoices = 1,
                    MaxChoices = 1,
                    DisplayOrder = 7,
                    TemplateId = template.Id,
                    Items = new[] { new ProductOptionItemDto { Name = "Item editado", Price = 99m } },
                },
            },
        }, ipAddress: null);

        result.NotFound.Should().BeFalse();
        var group = result.Product!.OptionGroups.Should().ContainSingle().Subject;
        group.TemplateId.Should().Be(template.Id);
        group.Name.Should().Be("Escolha um molho");
        group.ChoiceType.Should().Be("multiple");
        group.MinChoices.Should().Be(0);
        group.MaxChoices.Should().Be(2);
        group.IsRequired.Should().BeFalse();
        group.DisplayOrder.Should().Be(template.DisplayOrder);
        group.Items.Should().HaveCount(2);
        group.Items.Select(i => i.Name).Should().BeEquivalentTo(new[] { "Molho 1", "Molho 2" });
        group.Items.Select(i => i.Price).Should().BeEquivalentTo(new[] { 5m, 5.5m });

        // O template da loja permanece intacto.
        (await _db.ProductOptionGroupTemplates.CountAsync()).Should().Be(1);
        var persistedTemplate = await _db.ProductOptionGroupTemplates.Include(t => t.Items).SingleAsync();
        persistedTemplate.Name.Should().Be("Escolha um molho");
        persistedTemplate.Items.Select(i => i.Name).Should().BeEquivalentTo(new[] { "Molho 1", "Molho 2" });
    }

    [Fact]
    public async Task CreateAsync_ShouldRejectDuplicateTemplateIds()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var template = BuildTemplate(store.Id, "Escolha um molho", ("Molho 1", 5m));
        _db.ProductOptionGroupTemplates.Add(template);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Escolha um molho",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 2,
                    TemplateId = template.Id,
                    Items = Array.Empty<ProductOptionItemDto>(),
                },
                new ProductOptionGroupDto
                {
                    Name = "Escolha um molho",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 2,
                    TemplateId = template.Id,
                    Items = Array.Empty<ProductOptionItemDto>(),
                },
            },
        }, ipAddress: null);

        result.NotFound.Should().BeTrue();
        (await _db.Products.CountAsync()).Should().Be(0);
        (await _db.ProductOptionGroupTemplates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateIndependentTemplate_ForAuthoredGroupWithSameName()
    {
        var owner = Guid.NewGuid();
        var store = SeedStore(owner, "loja-a");
        var category = SeedCategory(store.Id);
        var existing = BuildTemplate(store.Id, "Bordas", ("Catupiry", 5m));
        _db.ProductOptionGroupTemplates.Add(existing);
        await _db.SaveChangesAsync();

        var result = await _sut.CreateAsync(owner, store.Id, new CreateProductRequestDto
        {
            CategoryId = category.Id,
            Name = "Pizza",
            Description = "Desc",
            Price = 40m,
            ImageUrl = "https://example.com/p.jpg",
            OptionGroups = new[]
            {
                new ProductOptionGroupDto
                {
                    Name = "Bordas",
                    ChoiceType = "multiple",
                    MinChoices = 0,
                    MaxChoices = 3,
                    Items = new[] { new ProductOptionItemDto { Name = "Cheddar", Price = 7m } },
                },
            },
        }, ipAddress: null);

        result.NotFound.Should().BeFalse();
        var group = result.Product!.OptionGroups.Should().ContainSingle().Subject;
        group.TemplateId.Should().NotBeNull();
        group.TemplateId.Should().NotBe(existing.Id);
        group.Items.Should().ContainSingle().Which.Name.Should().Be("Cheddar");

        var templates = await _db.ProductOptionGroupTemplates.Include(t => t.Items).ToListAsync();
        templates.Should().HaveCount(2);
        var authoredTemplate = templates.Single(t => t.Id == group.TemplateId);
        authoredTemplate.Items.Should().ContainSingle().Which.Name.Should().Be("Cheddar");

        // O template pré-existente permanece intacto.
        var original = templates.Single(t => t.Id == existing.Id);
        original.Items.Should().ContainSingle().Which.Name.Should().Be("Catupiry");
    }

    private Store SeedStore(Guid ownerUserId, string slug)
    {
        var store = new Store
        {
            OwnerUserId = ownerUserId,
            Name = "Loja Teste",
            Slug = slug,
            PhoneNumber = "11999999999",
        };
        _db.Stores.Add(store);
        return store;
    }

    private ProductCategory SeedCategory(Guid storeId)
    {
        var category = new ProductCategory
        {
            StoreId = storeId,
            Name = "Categoria",
            DisplayOrder = 1,
            IsActive = true,
        };
        _db.ProductCategories.Add(category);
        return category;
    }

    private static ProductOptionGroupTemplate BuildTemplate(Guid storeId, string name, params (string Name, decimal Price)[] items)
    {
        var template = new ProductOptionGroupTemplate
        {
            StoreId = storeId,
            Name = name,
            ChoiceType = "multiple",
            MinChoices = 0,
            MaxChoices = 2,
        };

        var order = 0;
        foreach (var (itemName, price) in items)
        {
            template.Items.Add(new ProductOptionItemTemplate
            {
                Name = itemName,
                Price = price,
                DisplayOrder = ++order,
            });
        }

        return template;
    }
}
