using FluentAssertions;
using Urbeat.Application.DTOs;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Services;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class PricingServiceTests
{
    private readonly PricingService _sut = new();

    private static Product SingleProduct(decimal price = 40m) => new()
    {
        Name = "Pizza",
        Price = price,
        SaleMode = "single",
    };

    private static Product SizeProduct(decimal variationPrice = 55m) => new()
    {
        Name = "Pizza",
        Price = 0m,
        SaleMode = "size",
        Variations =
        {
            new ProductVariation { Name = "Grande", Price = variationPrice, IsActive = true, IsDefault = true },
        },
    };

    private static Product FixedWeightProduct(decimal weightPrice = 22m) => new()
    {
        Name = "Frango a Passarinho",
        Price = 0m,
        SaleMode = "fixed_weight",
        Variations =
        {
            new ProductVariation { Name = "300 g", WeightGrams = 300, Price = weightPrice, IsActive = true, IsDefault = true },
        },
    };

    private static Product VariableWeightProduct(decimal pricePerKg = 59.90m, int minGrams = 200, int maxGrams = 2000, int increment = 100) => new()
    {
        Name = "Picanha",
        Price = 0m,
        SaleMode = "variable_weight",
        WeightConfig = new ProductWeightConfig
        {
            PricePerKg = pricePerKg,
            MinGrams = minGrams,
            MaxGrams = maxGrams,
            IncrementGrams = increment,
        },
    };

    // ── Single product ──────────────────────────────────────

    [Fact]
    public void PriceItem_ShouldReturnBasePrice_WhenNoSelections()
    {
        var product = SingleProduct();
        var result = _sut.PriceItem(product, new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 });

        result.IsValid.Should().BeTrue();
        result.UnitPrice.Should().Be(40m);
        result.ProductName.Should().Be("Pizza");
    }

    [Fact]
    public void PriceItem_ShouldSumAdditionals_IgnoringAnyClientPrice()
    {
        var product = SingleProduct();
        var bacon = new ProductAdditional { Name = "Bacon", Price = 5m, IsActive = true };
        product.Additionals.Add(bacon);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            AdditionalIds = new[] { bacon.Id },
        });

        result.UnitPrice.Should().Be(45m);
        result.ExtraNames.Should().Contain("Bacon");
    }

    [Fact]
    public void PriceItem_ShouldBeInvalid_WhenAdditionalDoesNotExist()
    {
        var product = SingleProduct();
        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            AdditionalIds = new[] { Guid.NewGuid() },
        });

        result.IsValid.Should().BeFalse();
    }

    // ── Size / Fixed Weight ─────────────────────────────────

    [Fact]
    public void PriceItem_ShouldUseVariationPrice_ForSizeProduct()
    {
        var product = SizeProduct(55m);
        var variation = product.Variations.First();

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            VariationId = variation.Id,
        });

        result.UnitPrice.Should().Be(55m);
        result.VariationName.Should().Be("Grande");
    }

    [Fact]
    public void PriceItem_ShouldRequireVariation_ForSizeProduct()
    {
        var product = SizeProduct();
        var result = _sut.PriceItem(product, new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("tamanho/peso");
    }

    [Fact]
    public void PriceItem_ShouldUseVariationPrice_ForFixedWeightProduct()
    {
        var product = FixedWeightProduct(22m);
        var variation = product.Variations.First();

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            VariationId = variation.Id,
        });

        result.UnitPrice.Should().Be(22m);
    }

    [Fact]
    public void PriceItem_ShouldRejectInactiveVariation()
    {
        var product = SizeProduct();
        var inactive = new ProductVariation { Name = "P", Price = 29m, IsActive = false };
        product.Variations.Add(inactive);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            VariationId = inactive.Id,
        });

        result.IsValid.Should().BeFalse();
    }

    // ── Variable Weight ─────────────────────────────────────

    [Fact]
    public void PriceItem_ShouldComputePriceByWeight_ForVariableWeightProduct()
    {
        var product = VariableWeightProduct(59.90m, 200, 2000, 100);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            WeightGrams = 500,
        });

        result.IsValid.Should().BeTrue();
        result.UnitPrice.Should().Be(29.95m); // 59.90 * 500 / 1000
        result.WeightGrams.Should().Be(500);
        result.WeightLabel.Should().Be("500 g");
    }

    [Fact]
    public void PriceItem_ShouldRejectWeightBelowMinimum()
    {
        var product = VariableWeightProduct(minGrams: 200);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            WeightGrams = 100,
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("mínimo");
    }

    [Fact]
    public void PriceItem_ShouldRejectWeightAboveMaximum()
    {
        var product = VariableWeightProduct(maxGrams: 2000);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            WeightGrams = 2500,
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("máximo");
    }

    [Fact]
    public void PriceItem_ShouldRejectWeightNotMatchingIncrement()
    {
        var product = VariableWeightProduct(minGrams: 200, increment: 100);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            WeightGrams = 350, // 200 + 150 → não é múltiplo de 100
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("incremento");
    }

    // ── Option Groups ───────────────────────────────────────

    [Fact]
    public void PriceItem_ShouldSumSelectedOptions()
    {
        var product = SingleProduct(50m);
        var group = new ProductOptionGroup
        {
            Name = "Adicionais",
            ChoiceType = "multiple",
            MinChoices = 0,
            MaxChoices = 3,
        };
        var bacon = new ProductOptionItem { Name = "Bacon", Price = 5m };
        var cheddar = new ProductOptionItem { Name = "Cheddar", Price = 4m };
        group.Items.Add(bacon);
        group.Items.Add(cheddar);
        product.OptionGroups.Add(group);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            OptionGroups = new[]
            {
                new CheckoutOptionGroupSelectionDto { GroupId = group.Id, ItemIds = new[] { bacon.Id, cheddar.Id } },
            },
        });

        result.UnitPrice.Should().Be(59m); // 50 + 5 + 4
        result.ExtraNames.Should().Contain(new[] { "Bacon", "Cheddar" });
    }

    [Fact]
    public void PriceItem_ShouldIncludeOptionPrices_ForChoiceAdditionalsAndGroups()
    {
        var product = SingleProduct(40m);
        var choice = new ProductChoiceOption { Name = "Com fritas", Price = 6m, IsActive = true };
        product.ChoiceOptions.Add(choice);
        var bacon = new ProductAdditional { Name = "Bacon", Price = 5m, IsActive = true };
        product.Additionals.Add(bacon);
        var group = new ProductOptionGroup
        {
            Name = "Adicionais",
            ChoiceType = "multiple",
            MinChoices = 0,
            MaxChoices = 3,
        };
        var cheddar = new ProductOptionItem { Name = "Cheddar", Price = 4m };
        group.Items.Add(cheddar);
        product.OptionGroups.Add(group);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            ChoiceOptionId = choice.Id,
            AdditionalIds = new[] { bacon.Id },
            OptionGroups = new[]
            {
                new CheckoutOptionGroupSelectionDto { GroupId = group.Id, ItemIds = new[] { cheddar.Id } },
            },
        });

        result.UnitPrice.Should().Be(55m); // 40 + 6 + 5 + 4
        result.OptionPrices.Should().Contain(x => x.Name == "Com fritas" && x.Price == 6m);
        result.OptionPrices.Should().Contain(x => x.Name == "Bacon" && x.Price == 5m);
        result.OptionPrices.Should().Contain(x => x.Name == "Cheddar" && x.Price == 4m);
        result.OptionPrices.Should().NotContain(x => x.Name == "Pizza");
    }

    [Fact]
    public void PriceItem_ShouldNotIncludeVariationPrice_AsOptionPrice()
    {
        var product = SizeProduct(55m);
        var variation = product.Variations.First();

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            VariationId = variation.Id,
        });

        result.UnitPrice.Should().Be(55m);
        result.VariationName.Should().Be("Grande");
        result.OptionPrices.Should().BeEmpty();
    }

    [Fact]
    public void PriceItem_ShouldAllowZeroPriceItem()
    {
        var product = SizeProduct(55m);
        var group = new ProductOptionGroup
        {
            Name = "Extras",
            ChoiceType = "multiple",
            MinChoices = 0,
            MaxChoices = 5,
        };
        var talheres = new ProductOptionItem { Name = "Talheres", Price = 0m };
        group.Items.Add(talheres);
        product.OptionGroups.Add(group);

        var variation = product.Variations.First();
        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            VariationId = variation.Id,
            OptionGroups = new[]
            {
                new CheckoutOptionGroupSelectionDto { GroupId = group.Id, ItemIds = new[] { talheres.Id } },
            },
        });

        result.UnitPrice.Should().Be(55m); // 55 + 0
        result.ExtraNames.Should().Contain("Talheres");
    }

    [Fact]
    public void PriceItem_ShouldBeInvalid_WhenRequiredGroupNotSelected()
    {
        var product = SingleProduct();
        var group = new ProductOptionGroup
        {
            Name = "Tamanho",
            ChoiceType = "single",
            MinChoices = 1,
            MaxChoices = 1,
        };
        group.Items.Add(new ProductOptionItem { Name = "P", Price = 30m });
        product.OptionGroups.Add(group);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto { ProductId = product.Id, Quantity = 1 });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Tamanho");
    }

    [Fact]
    public void PriceItem_ShouldBeInvalid_WhenExceedingMaxChoices()
    {
        var product = SingleProduct();
        var group = new ProductOptionGroup
        {
            Name = "Sabores",
            ChoiceType = "multiple",
            MinChoices = 0,
            MaxChoices = 1,
        };
        var a = new ProductOptionItem { Name = "A", Price = 1m };
        var b = new ProductOptionItem { Name = "B", Price = 2m };
        group.Items.Add(a);
        group.Items.Add(b);
        product.OptionGroups.Add(group);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            OptionGroups = new[]
            {
                new CheckoutOptionGroupSelectionDto { GroupId = group.Id, ItemIds = new[] { a.Id, b.Id } },
            },
        });

        result.IsValid.Should().BeFalse();
    }

    // ── Duplicate selections ────────────────────────────────

    [Fact]
    public void PriceItem_ShouldChargeDuplicateAdditionalOnlyOnce()
    {
        var product = SingleProduct(40m);
        var bacon = new ProductAdditional { Name = "Bacon", Price = 5m, IsActive = true };
        product.Additionals.Add(bacon);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            AdditionalIds = new[] { bacon.Id, bacon.Id },
        });

        result.IsValid.Should().BeTrue();
        result.UnitPrice.Should().Be(45m);
        result.ExtraNames.Should().ContainSingle().Which.Should().Be("Bacon");
        result.OptionPrices.Should().ContainSingle(x => x.Name == "Bacon" && x.Price == 5m);
    }

    [Fact]
    public void PriceItem_ShouldChargeDuplicateGroupItemOnlyOnce()
    {
        var product = SingleProduct(50m);
        var group = new ProductOptionGroup
        {
            Name = "Sabores",
            ChoiceType = "multiple",
            MinChoices = 0,
            MaxChoices = 3,
        };
        var bacon = new ProductOptionItem { Name = "Bacon", Price = 5m };
        group.Items.Add(bacon);
        product.OptionGroups.Add(group);

        var result = _sut.PriceItem(product, new CheckoutItemRequestDto
        {
            ProductId = product.Id,
            Quantity = 1,
            OptionGroups = new[]
            {
                new CheckoutOptionGroupSelectionDto { GroupId = group.Id, ItemIds = new[] { bacon.Id, bacon.Id } },
            },
        });

        result.IsValid.Should().BeTrue();
        result.UnitPrice.Should().Be(55m); // 50 + 5 (não 60)
        result.ExtraNames.Should().ContainSingle().Which.Should().Be("Bacon");
        result.OptionPrices.Should().ContainSingle(x => x.Name == "Bacon" && x.Price == 5m);
    }
}
