using System.Text;
using Microsoft.AspNetCore.Http;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Urbeat.WebApi.Uploads;

namespace Urbeat.UnitTests.WebApi;

public sealed class StoreMediaUploadValidatorTests
{
    private const string InvalidFormatMessage = "Formato de imagem não permitido.";
    private const string InvalidContentMessage = "O arquivo de imagem é inválido ou está corrompido.";

    [Theory]
    [InlineData("logo.svg", "image/svg+xml", "logo")]
    [InlineData("banner.avif", "image/avif", "banner")]
    [InlineData("logo.webp", "image/webp", "logo")]
    [InlineData("banner.jpg", "image/jpeg", "banner")]
    [InlineData("logo.jpg", "image/jpg", "logo")]
    [InlineData("banner.jpeg", "image/jpg", "banner")]
    [InlineData("logo.JPG", "image/jpeg", "logo")]
    [InlineData("product.png", "image/png", "products")]
    public void Accepts_supported_store_media_with_valid_content(string fileName, string contentType, string type)
    {
        var file = CreateFile(fileName, contentType, ValidContentFor(fileName));

        StoreMediaUploadValidator.Validate(file, type).Should().BeNull();
    }

    [Fact]
    public void Accepts_real_tiny_valid_svg_and_raster_files()
    {
        var svg = CreateFile("logo.svg", "image/svg+xml", SafeSvg());
        var png = CreateFile("logo.png", "image/png", Png());
        var jpeg = CreateFile("banner.jpg", "image/jpeg", Jpeg());
        var webp = CreateFile("banner.webp", "image/webp", Webp());
        var avif = CreateFile("banner.avif", "image/avif", Avif());

        StoreMediaUploadValidator.Validate(svg, "logo").Should().BeNull();
        StoreMediaUploadValidator.Validate(png, "logo").Should().BeNull();
        StoreMediaUploadValidator.Validate(jpeg, "banner").Should().BeNull();
        StoreMediaUploadValidator.Validate(webp, "banner").Should().BeNull();
        StoreMediaUploadValidator.Validate(avif, "banner").Should().BeNull();
    }

    [Fact]
    public void Rejects_truncated_real_raster_files_with_clear_content_error()
    {
        var truncatedPng = CreateFile("logo.png", "image/png", Png()[..(Png().Length / 2)]);
        var truncatedJpeg = CreateFile("banner.jpg", "image/jpeg", Jpeg()[..(Jpeg().Length / 2)]);
        var truncatedWebp = CreateFile("banner.webp", "image/webp", Webp()[..(Webp().Length / 2)]);
        var truncatedAvif = CreateFile("banner.avif", "image/avif", Avif()[..30]);

        StoreMediaUploadValidator.Validate(truncatedPng, "logo").Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(truncatedJpeg, "banner").Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(truncatedWebp, "banner").Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(truncatedAvif, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_png_with_trailing_bytes_after_iend()
    {
        var padded = Png().Concat(new byte[] { 0x00, 0x01, 0x02, 0x03 }).ToArray();
        var file = CreateFile("logo.png", "image/png", padded);

        StoreMediaUploadValidator.Validate(file, "logo").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_png_with_corrupted_chunk_crc()
    {
        var png = Png();
        var idatIndex = IndexOf(png, "IDAT");
        idatIndex.Should().BeGreaterThan(0);
        var idatLength = (png[idatIndex - 4] << 24) | (png[idatIndex - 3] << 16) | (png[idatIndex - 2] << 8) | png[idatIndex - 1];
        var crcIndex = idatIndex + 4 + idatLength;
        png[crcIndex] ^= 0xFF;
        var file = CreateFile("logo.png", "image/png", png);

        StoreMediaUploadValidator.Validate(file, "logo").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Accepts_png_with_valid_ancillary_chunk_before_iend()
    {
        var file = CreateFile("logo.png", "image/png", PngPaddedTo(Png().Length + 64));

        StoreMediaUploadValidator.Validate(file, "logo").Should().BeNull();
    }

    [Fact]
    public void Rejects_avif_with_empty_mdat_payload()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithEmptyMdat());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_with_corrupt_mdat_box()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithCorruptMdat());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_without_avif_brand()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithoutAvifBrand());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_without_meta_box()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithoutMeta());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_with_meta_missing_required_boxes()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithEmptyMeta());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Accepts_avif_with_extended_mdat_box_size()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithExtendedMdatSize());

        StoreMediaUploadValidator.Validate(file, "banner").Should().BeNull();
    }

    [Fact]
    public void Accepts_avif_with_extent_inside_mdat_payload()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithExtentInsideMdat(extentLength: 4));

        StoreMediaUploadValidator.Validate(file, "banner").Should().BeNull();
    }

    [Fact]
    public void Accepts_avif_with_version2_32bit_item_id_extent_inside_mdat()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithVersion2ExtentInsideMdat(extentLength: 4));

        StoreMediaUploadValidator.Validate(file, "banner").Should().BeNull();
    }

    [Fact]
    public void Rejects_avif_version2_with_truncated_item_id()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithVersion2TruncatedItemId());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_with_extent_outside_media_payload()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithExtentOutsideMediaPayload());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_with_extent_arithmetic_overflow()
    {
        var file = CreateFile("banner.avif", "image/avif", BuildAvif(OverflowingIlocPayload()));

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Accepts_avif_with_idat_relative_extent()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithIdatExtent(relativeOffset: 0, extentLength: 4));

        StoreMediaUploadValidator.Validate(file, "banner").Should().BeNull();
    }

    [Fact]
    public void Rejects_avif_with_idat_extent_outside_idat_payload()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithIdatExtent(relativeOffset: 100, extentLength: 4));

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_avif_when_only_non_primary_item_has_a_media_extent()
    {
        var file = CreateFile("banner.avif", "image/avif", AvifWithOnlyNonPrimaryExtentInMedia());

        StoreMediaUploadValidator.Validate(file, "banner").Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_header_only_raster_payloads_with_clear_content_error()
    {
        var pngHeaderOnly = CreateFile("logo.png", "image/png", new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        });
        var jpegHeaderOnly = CreateFile("banner.jpg", "image/jpeg", new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        });
        var webpHeaderOnly = CreateFile("banner.webp", "image/webp", Encoding.ASCII.GetBytes("RIFF\x24\x00\x00\x00WEBPVP8 "));
        var avifHeaderOnly = CreateFile("banner.avif", "image/avif", AvifFtypOnly());

        StoreMediaUploadValidator.Validate(pngHeaderOnly, "logo").Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(jpegHeaderOnly, "banner").Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(webpHeaderOnly, "banner").Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(avifHeaderOnly, "banner").Should().Be(InvalidContentMessage);
    }

    [Theory]
    [InlineData("logo.png", "image/png")]
    [InlineData("banner.jpg", "image/jpeg")]
    [InlineData("banner.webp", "image/webp")]
    [InlineData("banner.avif", "image/avif")]
    public void Rejects_arbitrary_bytes_with_clear_content_error(string fileName, string contentType)
    {
        var file = CreateFile(fileName, contentType, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        StoreMediaUploadValidator.Validate(file, "logo")
            .Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_raster_content_that_does_not_match_its_extension()
    {
        var pngBytesClaimedAsJpeg = CreateFile("banner.jpg", "image/jpeg", Png());
        var jpegBytesClaimedAsPng = CreateFile("logo.png", "image/png", Jpeg());
        var webpBytesClaimedAsPng = CreateFile("logo.png", "image/png", Webp());

        StoreMediaUploadValidator.Validate(pngBytesClaimedAsJpeg, "banner")
            .Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(jpegBytesClaimedAsPng, "logo")
            .Should().Be(InvalidContentMessage);
        StoreMediaUploadValidator.Validate(webpBytesClaimedAsPng, "logo")
            .Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Accepts_safe_svg_with_fragment_references()
    {
        var svg = CreateFile("logo.svg", "image/svg+xml", Encoding.UTF8.GetBytes(
            """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 10 10">
              <defs>
                <linearGradient id="grad"><stop offset="0" stop-color="#fff"/></linearGradient>
              </defs>
              <rect width="10" height="10" fill="url(#grad)"/>
              <use xlink:href="#grad"/>
            </svg>
            """));

        StoreMediaUploadValidator.Validate(svg, "logo").Should().BeNull();
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><foreignObject><body/></foreignObject></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><rect/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect onclick=\"alert(1)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"https://evil.example/x.png\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><use xlink:href=\"//evil.example/x.svg#a\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"javascript:alert(1)\">x</a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image src=\"data:image/svg+xml;base64,PHN2Zy8+\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@import url(https://evil.example/a.css);</style></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"background:url(http://evil.example/a.png)\"/></svg>")]
    [InlineData("<?xml-stylesheet href=\"https://evil.example/a.css\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&xxe;</svg>")]
    public void Rejects_unsafe_svg_payloads(string svg)
    {
        var file = CreateFile("logo.svg", "image/svg+xml", Encoding.UTF8.GetBytes(svg));

        StoreMediaUploadValidator.Validate(file, "logo")
            .Should().Be(InvalidContentMessage);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><iframe/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><object data=\"https://evil.example/x\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><embed src=\"https://evil.example/x\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><applet code=\"Evil\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><link href=\"https://evil.example/x\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><base href=\"https://evil.example/\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><handler/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><listener/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><svg:script xmlns:svg=\"http://www.w3.org/2000/svg\">alert(1)</svg:script></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><g><script>alert(1)</script></g></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"java&#9;script:alert(1)\">x</a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"java&#10;script:alert(1)\">x</a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"vbscript:msgbox(1)\">x</a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"&#106;avascript:alert(1)\">x</a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><a xlink:href=\"javascript:alert(1)\">x</a></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><use href=\"https://evil.example/x.svg#a\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect filter=\"url(http://evil.example/x.svg#f)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect clip-path=\"url(https://evil.example/x)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"url(data:image/png;base64,AAAA)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"behavior:url(#default#time2)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"-moz-binding:url(http://evil.example/x.xml#a)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"width:expression(alert(1))\"/></svg>")]
    public void Rejects_active_or_external_svg_content(string svg)
    {
        var file = CreateFile("logo.svg", "image/svg+xml", Encoding.UTF8.GetBytes(svg));

        StoreMediaUploadValidator.Validate(file, "logo")
            .Should().Be(InvalidContentMessage);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"5\" cy=\"5\" r=\"4\" fill=\"red\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><g transform=\"translate(1,2)\"><path d=\"M0 0 L10 10\"/></g></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0%\" stop-color=\"#000\"/></linearGradient></defs><rect fill=\"url(#g)\"/></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><text x=\"1\" y=\"1\">Ola</text></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><use href=\"#g\"/></svg>")]
    [InlineData("<svg><rect width=\"1\" height=\"1\"/></svg>")]
    public void Accepts_valid_simple_svg_documents(string svg)
    {
        var file = CreateFile("logo.svg", "image/svg+xml", Encoding.UTF8.GetBytes(svg));

        StoreMediaUploadValidator.Validate(file, "logo").Should().BeNull();
    }

    [Theory]
    [InlineData("<html><body>nope</body></html>")]
    [InlineData("<svg><unclosed></svg>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    public void Rejects_malformed_svg_or_svg_without_root(string svg)
    {
        var file = CreateFile("logo.svg", "image/svg+xml", Encoding.UTF8.GetBytes(svg));

        StoreMediaUploadValidator.Validate(file, "logo")
            .Should().Be(InvalidContentMessage);
    }

    [Fact]
    public void Rejects_unsupported_format()
    {
        var file = CreateFile("logo.gif", "image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38 });

        StoreMediaUploadValidator.Validate(file, "logo")
            .Should().Be(InvalidFormatMessage);
    }

    [Fact]
    public void Rejects_unknown_or_missing_extension_even_with_allowed_content_type()
    {
        var unknownExtension = CreateFile("logo.txt", "image/png", Png());
        var missingExtension = CreateFile("logo", "image/webp", Webp());
        var mismatchedType = CreateFile("banner.jpg", "image/png", Jpeg());

        StoreMediaUploadValidator.Validate(unknownExtension, "logo")
            .Should().Be(InvalidFormatMessage);
        StoreMediaUploadValidator.Validate(missingExtension, "banner")
            .Should().Be(InvalidFormatMessage);
        StoreMediaUploadValidator.Validate(mismatchedType, "banner")
            .Should().Be(InvalidFormatMessage);
    }

    [Fact]
    public void Accepts_missing_or_empty_content_type_when_extension_is_allowed()
    {
        var emptyType = CreateFile("logo.png", string.Empty, Png());
        var whitespaceType = CreateFile("banner.webp", "  ", Webp());

        StoreMediaUploadValidator.Validate(emptyType, "logo").Should().BeNull();
        StoreMediaUploadValidator.Validate(whitespaceType, "banner").Should().BeNull();
    }

    [Theory]
    [InlineData("logo.png", "logo")]
    [InlineData("banner.jpg", "banner")]
    [InlineData("banner.webp", "banner")]
    [InlineData("banner.avif", "banner")]
    [InlineData("logo.svg", "logo")]
    public void Accepts_application_octet_stream_as_unknown_type_when_content_matches_extension(
        string fileName,
        string type)
    {
        // Browsers upload MIME-less files as application/octet-stream; the frontend
        // accepts them as unknown, so the backend must agree and rely on sniffing.
        var file = CreateFile(fileName, "application/octet-stream", ValidContentFor(fileName));

        StoreMediaUploadValidator.Validate(file, type).Should().BeNull();
    }

    [Theory]
    [InlineData("logo.png", "image/webp")]
    [InlineData("banner.jpg", "image/png")]
    [InlineData("banner.webp", "image/jpeg")]
    [InlineData("banner.avif", "image/png")]
    public void Rejects_mime_types_that_do_not_match_the_extension(string fileName, string contentType)
    {
        var file = CreateFile(fileName, contentType, ValidContentFor(fileName));

        StoreMediaUploadValidator.Validate(file, "banner")
            .Should().Be(InvalidFormatMessage);
    }

    [Fact]
    public void Accepts_the_jpg_alias_for_jpeg_extensions()
    {
        var jpg = CreateFile("logo.jpg", "image/jpg", Jpeg());
        var jpeg = CreateFile("logo.jpeg", "image/jpg", Jpeg());

        StoreMediaUploadValidator.Validate(jpg, "logo").Should().BeNull();
        StoreMediaUploadValidator.Validate(jpeg, "logo").Should().BeNull();
    }

    [Fact]
    public void Enforces_logo_and_banner_limits()
    {
        var oversizedLogo = CreateFile("logo.png", "image/png", PngPaddedTo(2 * 1024 * 1024 + 1, Png()));
        var oversizedBanner = CreateFile("banner.png", "image/png", PngPaddedTo(5 * 1024 * 1024 + 1, Png()));

        StoreMediaUploadValidator.Validate(oversizedLogo, "logo")
            .Should().Be("A logo deve ter no máximo 2 MB.");
        StoreMediaUploadValidator.Validate(oversizedBanner, "banner")
            .Should().Be("O banner deve ter no máximo 5 MB.");
    }

    [Fact]
    public void Accepts_logo_and_banner_at_the_exact_size_boundary()
    {
        var logoAtLimit = CreateFile("logo.png", "image/png", PngPaddedTo(2 * 1024 * 1024, Png()));
        var bannerAtLimit = CreateFile("banner.png", "image/png", PngPaddedTo(5 * 1024 * 1024, Png()));

        StoreMediaUploadValidator.Validate(logoAtLimit, "logo").Should().BeNull();
        StoreMediaUploadValidator.Validate(bannerAtLimit, "banner").Should().BeNull();
    }

    [Fact]
    public void Enforces_product_six_megabyte_limit_from_api_contract()
    {
        var productAtLimit = CreateFile("product.png", "image/png", PngPaddedTo(6 * 1024 * 1024, Png()));
        var productOverLimit = CreateFile("product.png", "image/png", PngPaddedTo(6 * 1024 * 1024 + 1, Png()));

        StoreMediaUploadValidator.Validate(productAtLimit, "products").Should().BeNull();
        StoreMediaUploadValidator.Validate(productOverLimit, "products")
            .Should().Be("A imagem do produto deve ter no máximo 6 MB.");
    }

    [Fact]
    public void Enforces_the_default_product_limit_for_store_media_type()
    {
        var overDefault = CreateFile("asset.png", "image/png", PngPaddedTo(6 * 1024 * 1024 + 1, Png()));

        StoreMediaUploadValidator.Validate(overDefault, "store-media")
            .Should().Be("A imagem do produto deve ter no máximo 6 MB.");
    }

    private static byte[] ValidContentFor(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => Png(),
            ".jpg" or ".jpeg" => Jpeg(),
            ".webp" => Webp(),
            ".avif" => Avif(),
            ".svg" => SafeSvg(),
            _ => Array.Empty<byte>(),
        };

    private static byte[] SafeSvg()
        => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");

    private static byte[] Png()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] Jpeg()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] Webp()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsWebp(stream);
        return stream.ToArray();
    }

    // A real 1x1 AVIF (314 bytes) produced by libavif via avifenc. ImageSharp has no
    // AVIF decoder and no safe managed decoder package can be added (see validator),
    // so the validator parses the AVIF/ISOBMFF structure and this fixture proves the
    // structural rules accept genuine files while the corrupt-mdat variants are rejected.
    private const string RealAvifBase64 =
        "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUEAAADybWV0YQAAAAAAAAAoaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAGxpYmF2aWYAAAAADnBpdG0AAAAAAAEAAAAeaWxvYwAAAABEAAABAAEAAAABAAABGgAAACAAAAAoaWluZgAAAAAAAQAAABppbmZlAgAAAAABAABhdjAxQ29sb3IAAAAAamlwcnAAAABLaXBjbwAAABRpc3BlAAAAAAAAAAEAAAABAAAAEHBpeGkAAAAAAwgICAAAAAxhdjFDgSAAAAAAABNjb2xybmNseAABAA0ABoAAAAAXaXBtYQAAAAAAAAABAAEEAQKDBAAAAChtZGF0EgAKBzgABhAQ0GkyExSABBBBBAAAeUzUa/J2pXcGEYA=";

    private static byte[] Avif() => RealAvif();

    private static byte[] RealAvif() => Convert.FromBase64String(RealAvifBase64);

    private static byte[] AvifWithEmptyMdat()
    {
        var real = RealAvif();
        var mdatHeaderStart = IndexOf(real, "mdat") - 4;
        var bytes = new byte[mdatHeaderStart + 8];
        Array.Copy(real, bytes, bytes.Length);
        WriteBigEndian(bytes, mdatHeaderStart, 8);
        return bytes;
    }

    private static byte[] AvifWithCorruptMdat()
    {
        var real = RealAvif();
        var bytes = (byte[])real.Clone();
        WriteBigEndian(bytes, IndexOf(real, "mdat") - 4, real.Length);
        return bytes;
    }

    private static byte[] AvifWithoutAvifBrand()
    {
        var bytes = (byte[])RealAvif().Clone();
        var brand = Encoding.ASCII.GetBytes("avif");
        var replacement = Encoding.ASCII.GetBytes("heic");
        for (var index = 0; index + brand.Length <= bytes.Length; index++)
        {
            if (bytes[index] == brand[0]
                && bytes[index + 1] == brand[1]
                && bytes[index + 2] == brand[2]
                && bytes[index + 3] == brand[3])
            {
                replacement.CopyTo(bytes, index);
            }
        }

        return bytes;
    }

    private static byte[] AvifWithoutMeta()
    {
        var real = RealAvif();
        var ftypSize = ReadBigEndianInt32(real, 0);
        return real[..ftypSize].Concat(Box("mdat", new byte[] { 1, 2, 3, 4 })).ToArray();
    }

    private static byte[] AvifWithEmptyMeta()
    {
        var real = RealAvif();
        var ftypSize = ReadBigEndianInt32(real, 0);
        return real[..ftypSize]
            .Concat(Box("meta", new byte[4]))
            .Concat(Box("mdat", new byte[] { 1, 2, 3, 4 }))
            .ToArray();
    }

    private static byte[] AvifWithExtendedMdatSize()
    {
        var real = RealAvif();
        var mdatStart = IndexOf(real, "mdat") - 4;
        var payload = real[(mdatStart + 8)..];
        var result = new byte[mdatStart + 16 + payload.Length];
        Array.Copy(real, 0, result, 0, mdatStart);
        result[mdatStart] = 0;
        result[mdatStart + 1] = 0;
        result[mdatStart + 2] = 0;
        result[mdatStart + 3] = 1;
        Encoding.ASCII.GetBytes("mdat").CopyTo(result, mdatStart + 4);
        WriteBigEndian64(result, mdatStart + 8, 16L + payload.Length);
        payload.CopyTo(result, mdatStart + 16);

        // The extended header adds 8 bytes, so repoint the item extent at the
        // shifted payload to keep the fixture a structurally valid AVIF.
        var ilocPayloadStart = IndexOf(result, "iloc") + 4;
        WriteBigEndian(result, ilocPayloadStart + 14, mdatStart + 16);
        return result;
    }

    private static byte[] AvifWithExtentOutsideMediaPayload()
    {
        // Fabricated `iloc` whose extent stays inside the file but points into the
        // `meta` box instead of the `mdat` media payload.
        var real = RealAvif();
        var bytes = (byte[])real.Clone();
        var ilocPayloadStart = IndexOf(real, "iloc") + 4;
        var extentOffsetIndex = ilocPayloadStart + 14;
        WriteBigEndian(bytes, extentOffsetIndex, 100);
        WriteBigEndian(bytes, extentOffsetIndex + 4, 4);
        return bytes;
    }

    private static byte[] AvifWithExtentInsideMdat(int extentLength)
    {
        var ilocPayload = new byte[22];
        ilocPayload[4] = 0x44; // offset_size = 4, length_size = 4
        ilocPayload[5] = 0x00; // base_offset_size = 0
        ilocPayload[7] = 1;    // item_count
        ilocPayload[9] = 1;    // item_ID
        ilocPayload[13] = 1;   // extent_count
        WriteBigEndian(ilocPayload, 18, extentLength);

        var avif = BuildAvif(ilocPayload, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var mdatPayloadStart = IndexOf(avif, "mdat") + 4;
        var ilocPayloadStart = IndexOf(avif, "iloc") + 4;
        WriteBigEndian(avif, ilocPayloadStart + 14, mdatPayloadStart);
        return avif;
    }

    private static byte[] AvifWithVersion2ExtentInsideMdat(int extentLength)
    {
        // iloc version 2 widens item_ID to 32 bits. Layout:
        // [0]=version, [4]=sizes, [5]=base/index, [6..7]=item_count,
        // [8..11]=item_ID (32-bit), [12..13]=construction_method,
        // [14..15]=data_reference_index, [16..17]=extent_count,
        // [18..21]=extent_offset, [22..25]=extent_length.
        var ilocPayload = new byte[26];
        ilocPayload[0] = 2;    // version 2
        ilocPayload[4] = 0x44; // offset_size = 4, length_size = 4
        ilocPayload[5] = 0x00; // base_offset_size = 0, index_size = 0
        ilocPayload[7] = 1;    // item_count
        WriteBigEndian(ilocPayload, 8, 1); // item_ID = 1
        ilocPayload[17] = 1;   // extent_count
        WriteBigEndian(ilocPayload, 22, extentLength);

        var avif = BuildAvif(ilocPayload, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var mdatPayloadStart = IndexOf(avif, "mdat") + 4;
        var ilocPayloadStart = IndexOf(avif, "iloc") + 4;
        WriteBigEndian(avif, ilocPayloadStart + 18, mdatPayloadStart);
        return avif;
    }

    private static byte[] AvifWithVersion2TruncatedItemId()
    {
        // Declares one item but only provides two of the four version-2 item_ID bytes.
        var ilocPayload = new byte[10];
        ilocPayload[0] = 2;    // version 2
        ilocPayload[4] = 0x44; // offset_size = 4, length_size = 4
        ilocPayload[5] = 0x00; // base_offset_size = 0, index_size = 0
        ilocPayload[7] = 1;    // item_count
        return BuildAvif(ilocPayload);
    }

    private static byte[] AvifWithIdatExtent(int relativeOffset, int extentLength, int idatLength = 8)
    {
        var ilocPayload = new byte[24];
        ilocPayload[0] = 1;    // version 1
        ilocPayload[4] = 0x44; // offset_size = 4, length_size = 4
        ilocPayload[5] = 0x00; // base_offset_size = 0, index_size = 0
        ilocPayload[7] = 1;    // item_count
        ilocPayload[9] = 1;    // item_ID
        ilocPayload[11] = 1;   // construction_method = 1 (idat-relative)
        ilocPayload[15] = 1;   // extent_count
        WriteBigEndian(ilocPayload, 16, relativeOffset);
        WriteBigEndian(ilocPayload, 20, extentLength);

        var idatPayload = Enumerable.Range(0, idatLength).Select(value => (byte)value).ToArray();
        var metaPayload = new byte[] { 0, 0, 0, 0 }
            .Concat(Box("hdlr", PictureHandler()))
            .Concat(Box("pitm", new byte[] { 0, 0, 0, 0, 0, 1 }))
            .Concat(Box("iloc", ilocPayload))
            .Concat(Box("iinf", Av1ItemInfo()))
            .Concat(Box("idat", idatPayload))
            .ToArray();

        return AvifFtyp().Concat(Box("meta", metaPayload)).ToArray();
    }

    private static byte[] AvifWithOnlyNonPrimaryExtentInMedia()
    {
        var ilocPayload = new byte[36];
        ilocPayload[4] = 0x44; // offset_size = 4, length_size = 4
        ilocPayload[5] = 0x00; // base_offset_size = 0
        ilocPayload[7] = 2;    // item_count
        // item 1 (primary): extent points into `meta`, not the media payload.
        ilocPayload[9] = 1;    // item_ID
        ilocPayload[13] = 1;   // extent_count
        WriteBigEndian(ilocPayload, 14, 100);
        WriteBigEndian(ilocPayload, 18, 4);
        // item 2: extent is patched to the `mdat` payload.
        ilocPayload[23] = 2;   // item_ID
        ilocPayload[27] = 1;   // extent_count
        WriteBigEndian(ilocPayload, 32, 4);

        var avif = BuildAvif(ilocPayload, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var mdatPayloadStart = IndexOf(avif, "mdat") + 4;
        var ilocPayloadStart = IndexOf(avif, "iloc") + 4;
        WriteBigEndian(avif, ilocPayloadStart + 28, mdatPayloadStart);
        return avif;
    }

    private static byte[] OverflowingIlocPayload()
    {
        // base_offset = extent_offset = long.MaxValue and length = 2 make the old
        // unchecked `base + offset + length` wrap to 0 and pass the file-bounds test.
        var payload = new byte[38];
        payload[4] = 0x88; // offset_size = 8, length_size = 8
        payload[5] = 0x80; // base_offset_size = 8, reserved = 0
        payload[7] = 1;    // item_count
        payload[9] = 1;    // item_ID
        WriteBigEndian64(payload, 12, long.MaxValue); // base_offset
        payload[21] = 1;   // extent_count
        WriteBigEndian64(payload, 22, long.MaxValue); // extent_offset
        WriteBigEndian64(payload, 30, 2);             // extent_length
        return payload;
    }

    private static byte[] BuildAvif(byte[] ilocPayload, byte[]? mdatPayload = null)
    {
        mdatPayload ??= new byte[] { 1, 2, 3, 4 };
        var metaPayload = new byte[] { 0, 0, 0, 0 }
            .Concat(Box("hdlr", PictureHandler()))
            .Concat(Box("pitm", new byte[] { 0, 0, 0, 0, 0, 1 }))
            .Concat(Box("iloc", ilocPayload))
            .Concat(Box("iinf", Av1ItemInfo()))
            .ToArray();

        return AvifFtyp()
            .Concat(Box("meta", metaPayload))
            .Concat(Box("mdat", mdatPayload))
            .ToArray();
    }

    private static byte[] AvifFtyp()
        => Box("ftyp", Encoding.ASCII.GetBytes("avif")
            .Concat(new byte[] { 0, 0, 0, 0 })
            .Concat(Encoding.ASCII.GetBytes("avif"))
            .ToArray());

    private static byte[] PictureHandler()
    {
        var payload = new byte[12];
        Encoding.ASCII.GetBytes("pict").CopyTo(payload, 8);
        return payload;
    }

    private static byte[] Av1ItemInfo()
    {
        var payload = new byte[14];
        Encoding.ASCII.GetBytes("av01").CopyTo(payload, 10);
        return payload;
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        WriteBigEndian(bytes, 0, bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static int ReadBigEndianInt32(byte[] content, int offset)
        => (content[offset] << 24)
            | (content[offset + 1] << 16)
            | (content[offset + 2] << 8)
            | content[offset + 3];

    private static void WriteBigEndian64(byte[] buffer, int offset, long value)
    {
        buffer[offset] = (byte)(value >> 56);
        buffer[offset + 1] = (byte)(value >> 48);
        buffer[offset + 2] = (byte)(value >> 40);
        buffer[offset + 3] = (byte)(value >> 32);
        buffer[offset + 4] = (byte)(value >> 24);
        buffer[offset + 5] = (byte)(value >> 16);
        buffer[offset + 6] = (byte)(value >> 8);
        buffer[offset + 7] = (byte)value;
    }

    private static byte[] AvifFtypOnly()
    {
        var bytes = new byte[24];
        WriteBigEndian(bytes, 0, 24);
        Encoding.ASCII.GetBytes("ftyp").CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("avif").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("avif").CopyTo(bytes, 16);
        return bytes;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    // Grows a valid PNG by inserting a real ancillary tEXt chunk before IEND, so the
    // size-boundary fixtures stay structurally valid instead of appending bytes after IEND.
    private static byte[] PngPaddedTo(int length, byte[]? basePng = null)
    {
        basePng ??= Png();
        const int iendLength = 12;
        const int chunkOverhead = 12;
        var dataLength = length - basePng.Length - chunkOverhead;
        if (dataLength < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Padding must exceed the base PNG size.");
        }

        var iendStart = basePng.Length - iendLength;
        var chunkData = new byte[dataLength];
        Encoding.ASCII.GetBytes("pad").CopyTo(chunkData, 0);
        chunkData[3] = 0;

        var chunk = BuildPngChunk("tEXt", chunkData);
        var result = new byte[length];
        Array.Copy(basePng, 0, result, 0, iendStart);
        Array.Copy(chunk, 0, result, iendStart, chunk.Length);
        Array.Copy(basePng, iendStart, result, iendStart + chunk.Length, iendLength);
        return result;
    }

    private static byte[] BuildPngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        WriteBigEndian(chunk, 0, data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        var crc = Crc32(chunk.AsSpan(4, 4 + data.Length));
        WriteBigEndian(chunk, 8 + data.Length, unchecked((int)crc));
        return chunk;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }

    private static int IndexOf(byte[] content, string ascii)
    {
        var needle = Encoding.ASCII.GetBytes(ascii);
        for (var i = 0; i + needle.Length <= content.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (content[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static IFormFile CreateFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }
}
