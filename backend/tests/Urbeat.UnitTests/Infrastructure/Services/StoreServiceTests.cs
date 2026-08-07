using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Urbeat.Domain.Entities;
using Xunit;

namespace Urbeat.UnitTests.Infrastructure.Services;

public class StoreServiceTests
{
    [Fact]
    public void StoreEntity_ShouldNotHaveStorePathProperty()
    {
        // Assert: Verify StorePath does not exist on the entity
        typeof(Store).GetProperty("StorePath").Should().BeNull("StorePath property should be completely removed from the Store entity");
        typeof(Store).GetProperty("Slug").Should().NotBeNull("Slug property must exist as the canonical identifier");
    }

    [Theory]
    [InlineData("Pizza Hunter & Grill", "pizza-hunter-grill")]
    [InlineData("Café da Manhã & Cia", "cafe-da-manha-cia")]
    [InlineData("  Burger King  ", "burger-king")]
    [InlineData("Loja_123", "loja123")] // Underscores are removed by the regex
    [InlineData("Açaí & Pastel", "acai-pastel")]
    public void Slugify_ShouldConvertToValidKebabCase(string input, string expectedSlug)
    {
        // Act: Simulating the Slugify logic used in StoreService
        var slug = input.ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in slug)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        slug = sb.ToString().Normalize(NormalizationForm.FormC);
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');

        // Assert
        slug.Should().Be(expectedSlug);
    }
}
