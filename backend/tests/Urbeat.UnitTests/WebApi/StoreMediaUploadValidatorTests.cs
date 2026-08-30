using Microsoft.AspNetCore.Http;
using FluentAssertions;
using Urbeat.WebApi.Uploads;

namespace Urbeat.UnitTests.WebApi;

public sealed class StoreMediaUploadValidatorTests
{
    [Theory]
    [InlineData("logo.svg", "image/svg+xml", "logo")]
    [InlineData("banner.avif", "image/avif", "banner")]
    [InlineData("logo.webp", "image/webp", "logo")]
    [InlineData("banner.jpg", "image/jpeg", "banner")]
    public void Accepts_supported_store_media(string fileName, string contentType, string type)
    {
        var file = CreateFile(fileName, contentType, 1);

        StoreMediaUploadValidator.Validate(file, type).Should().BeNull();
    }

    [Fact]
    public void Rejects_unsupported_format()
    {
        var file = CreateFile("logo.gif", "image/gif", 1);

        StoreMediaUploadValidator.Validate(file, "logo")
            .Should().Be("Formato de imagem não permitido.");
    }

    [Fact]
    public void Enforces_logo_and_banner_limits()
    {
        var oversizedLogo = CreateFile("logo.png", "image/png", 2 * 1024 * 1024 + 1);
        var oversizedBanner = CreateFile("banner.png", "image/png", 5 * 1024 * 1024 + 1);

        StoreMediaUploadValidator.Validate(oversizedLogo, "logo")
            .Should().Be("A logo deve ter no máximo 2 MB.");
        StoreMediaUploadValidator.Validate(oversizedBanner, "banner")
            .Should().Be("O banner deve ter no máximo 5 MB.");
    }

    private static IFormFile CreateFile(string fileName, string contentType, int length)
    {
        var file = new FormFile(new MemoryStream(new byte[length]), 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
        return file;
    }
}
