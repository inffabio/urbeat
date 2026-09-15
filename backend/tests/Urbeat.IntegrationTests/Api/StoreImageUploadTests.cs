using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Urbeat.Application.DTOs;
using Urbeat.IntegrationTests.Infrastructure;

namespace Urbeat.IntegrationTests.Api;

public sealed class StoreImageUploadTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public StoreImageUploadTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadImage_Accepts_valid_tiny_png_logo()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Png(), "logo.png", "image/png");
        var response = await client.PostAsync("/api/stores/upload-image?type=logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadImage_Accepts_valid_png_with_octet_stream_content_type()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Png(), "logo.png", "application/octet-stream");
        var response = await client.PostAsync("/api/stores/upload-image?type=logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadImage_Accepts_valid_png_without_content_type()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Png(), "logo.png", null);
        var response = await client.PostAsync("/api/stores/upload-image?type=logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadImage_Accepts_valid_tiny_avif_banner()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Avif(), "banner.avif", "image/avif");
        var response = await client.PostAsync("/api/stores/upload-image?type=banner", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("evil")]
    [InlineData("")]
    [InlineData("../logo")]
    [InlineData("products/../../logo")]
    [InlineData("store_media")]
    public async Task UploadImage_rejects_unsupported_type_with_clear_error(string type)
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Png(), "logo.png", "image/png");
        var response = await client.PostAsync(
            $"/api/stores/upload-image?type={Uri.EscapeDataString(type)}",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("Tipo de mídia inválido");
        message.Should().NotContain("6 MB");
    }

    [Fact]
    public async Task UploadImage_accepts_default_store_media_type_when_query_is_omitted()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Png(), "asset.png", "image/png");
        var response = await client.PostAsync("/api/stores/upload-image", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadImage_rejects_avif_with_empty_mdat()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(AvifWithEmptyMdat(), "banner.avif", "image/avif");
        var response = await client.PostAsync("/api/stores/upload-image?type=banner", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("inválido");
    }

    [Fact]
    public async Task UploadImage_rejects_avif_with_extent_outside_media_payload()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(AvifWithExtentOutsideMediaPayload(), "banner.avif", "image/avif");
        var response = await client.PostAsync("/api/stores/upload-image?type=banner", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("inválido");
    }

    [Fact]
    public async Task UploadImage_rejects_truncated_content_with_clear_error()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(new byte[] { 0x01, 0x02, 0x03 }, "logo.png", "image/png");
        var response = await client.PostAsync("/api/stores/upload-image?type=logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("inválido");
    }

    [Fact]
    public async Task UploadImage_rejects_header_only_png_with_clear_error()
    {
        var client = await CreateAuthenticatedSellerAsync();
        var headerOnly = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        using var content = BuildFileContent(headerOnly, "logo.png", "image/png");
        var response = await client.PostAsync("/api/stores/upload-image?type=logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("inválido");
    }

    [Fact]
    public async Task UploadImage_rejects_mismatched_mime_type_for_extension()
    {
        var client = await CreateAuthenticatedSellerAsync();

        using var content = BuildFileContent(Png(), "logo.png", "image/webp");
        var response = await client.PostAsync("/api/stores/upload-image?type=logo", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("Formato");
    }

    [Fact]
    public async Task UploadImage_rejects_product_over_six_megabytes()
    {
        var client = await CreateAuthenticatedSellerAsync();

        var oversized = new byte[6 * 1024 * 1024 + 1];
        Png().CopyTo(oversized, 0);

        using var content = BuildFileContent(oversized, "product.png", "image/png");
        var response = await client.PostAsync("/api/stores/upload-image?type=products", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("6 MB");
    }

    [Fact]
    public async Task ProductImageEndpoint_rejects_invalid_content()
    {
        var client = await CreateAuthenticatedSellerAsync();
        var (storeId, productId) = await CreateStoreAndProductAsync(client);

        using var content = BuildFileContent(new byte[] { 0x01, 0x02, 0x03 }, "product.png", "image/png");
        var response = await client.PostAsync($"/api/stores/{storeId}/products/{productId}/images", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await ReadErrorMessageAsync(response);
        message.Should().Contain("inválido");
    }

    [Fact]
    public async Task ProductImageEndpoint_accepts_valid_image()
    {
        var client = await CreateAuthenticatedSellerAsync();
        var (storeId, productId) = await CreateStoreAndProductAsync(client);

        using var content = BuildFileContent(Png(), "product.png", "image/png");
        var response = await client.PostAsync($"/api/stores/{storeId}/products/{productId}/images", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> CreateAuthenticatedSellerAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var unique = Guid.NewGuid().ToString("N");
        var email = $"images.flow.{unique}@urbeat.local";
        const string password = "SenhaForte123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register/seller", new RegisterUserRequestDto
        {
            FullName = $"Seller Images {unique}",
            Email = email,
            Password = password,
            PhoneNumber = "11984443333"
        });
        registerResponse.IsSuccessStatusCode.Should().BeTrue(
            $"registration failed: {registerResponse.StatusCode} {await registerResponse.Content.ReadAsStringAsync()}");
        await _factory.ConfirmEmailAsync(email);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/seller", new LoginRequestDto
        {
            Email = email,
            Password = password
        });

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private async Task<(Guid StoreId, Guid ProductId)> CreateStoreAndProductAsync(HttpClient client)
    {
        var createStoreResponse = await client.PostAsJsonAsync("/api/stores", new CreateStoreRequestDto
        {
            Name = "Loja Imagens",
            Slug = $"loja-imagens-{Guid.NewGuid():N}",
            PhoneNumber = "11982221111",
            CuisineType = "Brasileira",
            MaxDeliveryRadiusKm = 5,
        });
        var store = await createStoreResponse.Content.ReadFromJsonAsync<StoreResponseDto>();

        var categoryResponse = await client.PostAsJsonAsync($"/api/stores/{store!.Id}/categories", new CreateProductCategoryRequestDto
        {
            Name = "Categoria Teste",
            DisplayOrder = 1
        });
        var category = await categoryResponse.Content.ReadFromJsonAsync<ProductCategoryResponseDto>();

        var productResponse = await client.PostAsJsonAsync($"/api/stores/{store.Id}/products", new CreateProductRequestDto
        {
            CategoryId = category!.Id,
            Name = "Produto Imagem",
            Price = 10m,
            ImageUrl = "https://example.com/p.jpg",
            DisplayOrder = 1
        });
        var product = await productResponse.Content.ReadFromJsonAsync<ProductResponseDto>();

        return (store.Id, product!.Id);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            return error.GetString() ?? json;
        }

        if (root.TryGetProperty("detail", out var detail))
        {
            return detail.GetString() ?? json;
        }

        return json;
    }

    private static MultipartFormDataContent BuildFileContent(byte[] bytes, string fileName, string? contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        content.Add(fileContent, "file", fileName);
        return content;
    }

    private static byte[] Png()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    // Real 1x1 AVIF produced by libavif; ImageSharp has no AVIF decoder, so the
    // validator verifies the ISOBMFF/HEIF structure instead of decoding pixels.
    private const string RealAvifBase64 =
        "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUEAAADybWV0YQAAAAAAAAAoaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAGxpYmF2aWYAAAAADnBpdG0AAAAAAAEAAAAeaWxvYwAAAABEAAABAAEAAAABAAABGgAAACAAAAAoaWluZgAAAAAAAQAAABppbmZlAgAAAAABAABhdjAxQ29sb3IAAAAAamlwcnAAAABLaXBjbwAAABRpc3BlAAAAAAAAAAEAAAABAAAAEHBpeGkAAAAAAwgICAAAAAxhdjFDgSAAAAAAABNjb2xybmNseAABAA0ABoAAAAAXaXBtYQAAAAAAAAABAAEEAQKDBAAAAChtZGF0EgAKBzgABhAQ0GkyExSABBBBBAAAeUzUa/J2pXcGEYA=";

    private static byte[] Avif() => Convert.FromBase64String(RealAvifBase64);

    private static byte[] AvifWithEmptyMdat()
    {
        var real = Avif();
        var mdatHeaderStart = IndexOf(real, "mdat") - 4;
        var bytes = new byte[mdatHeaderStart + 8];
        Array.Copy(real, bytes, bytes.Length);
        bytes[mdatHeaderStart] = 0;
        bytes[mdatHeaderStart + 1] = 0;
        bytes[mdatHeaderStart + 2] = 0;
        bytes[mdatHeaderStart + 3] = 8;
        return bytes;
    }

    private static byte[] AvifWithExtentOutsideMediaPayload()
    {
        // Keeps the extent inside the file but points it into the `meta` box rather
        // than the `mdat` media payload.
        var real = Avif();
        var bytes = (byte[])real.Clone();
        var extentOffsetIndex = IndexOf(real, "iloc") + 4 + 14;
        bytes[extentOffsetIndex] = 0;
        bytes[extentOffsetIndex + 1] = 0;
        bytes[extentOffsetIndex + 2] = 0;
        bytes[extentOffsetIndex + 3] = 100;
        bytes[extentOffsetIndex + 4] = 0;
        bytes[extentOffsetIndex + 5] = 0;
        bytes[extentOffsetIndex + 6] = 0;
        bytes[extentOffsetIndex + 7] = 4;
        return bytes;
    }

    private static int IndexOf(byte[] content, string ascii)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(ascii);
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
}
