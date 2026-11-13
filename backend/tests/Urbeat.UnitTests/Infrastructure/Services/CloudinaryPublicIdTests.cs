using FluentAssertions;
using Urbeat.Infrastructure.Services;
using Xunit;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class CloudinaryPublicIdTests
{
    [Theory]
    [InlineData(
        "https://res.cloudinary.com/urbeat/image/upload/v1712345678/stores/loja/logo/file.jpg",
        "stores/loja/logo/file")]
    [InlineData(
        "https://res.cloudinary.com/urbeat/image/upload/stores/loja/banner/file.png",
        "stores/loja/banner/file")]
    [InlineData(
        "https://res.cloudinary.com/urbeat/image/upload/c_limit,w_1920/v1712345678/stores/loja/products/file.webp",
        "stores/loja/products/file")]
    [InlineData(
        "https://res.cloudinary.com/urbeat/image/upload/v1/stores/_pending/user-id/store-media/file.avif?foo=bar",
        "stores/_pending/user-id/store-media/file")]
    public void ExtractFromUrl_ShouldReturnFolderPreservingPublicId(string url, string expected)
    {
        CloudinaryPublicId.ExtractFromUrl(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("https://example.com/stores/loja/logo.png")]
    [InlineData("https://example.com/upload/v1/stores/loja/logo.png")]
    [InlineData("https://res.cloudinary.com/urbeat/image/upload")]
    public void ExtractFromUrl_ShouldReturnEmpty_WhenUrlIsNotADeletableCloudinaryAsset(string url)
    {
        CloudinaryPublicId.ExtractFromUrl(url).Should().BeEmpty();
    }

    [Theory]
    [InlineData("stores/loja/logo/file", true)]
    [InlineData("stores/_pending/user/logo/file", true)]
    [InlineData("stores", false)]
    [InlineData("stores-old/loja/file", false)]
    [InlineData("other/stores/loja/file", false)]
    [InlineData("", false)]
    public void IsUnderPrefix_ShouldOnlyAcceptTheFixedPrefixWithRemainder(string publicId, bool expected)
    {
        CloudinaryPublicId.IsUnderPrefix(publicId, CloudinaryPublicId.StorePrefix).Should().Be(expected);
    }

    [Theory]
    [InlineData("stores/", "stores/")]
    [InlineData("stores", "stores/")]
    [InlineData("/stores", "stores/")]
    [InlineData(" stores/ ", "stores/")]
    public void NormalizePrefix_ShouldReturnPrefixWithSingleTrailingSlash(string prefix, string expected)
    {
        CloudinaryPublicId.NormalizePrefix(prefix).Should().Be(expected);
    }
}
