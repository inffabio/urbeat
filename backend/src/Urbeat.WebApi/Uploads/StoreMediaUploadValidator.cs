using Microsoft.AspNetCore.Http;

namespace Urbeat.WebApi.Uploads;

public static class StoreMediaUploadValidator
{
    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".avif"] = "image/avif",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".svg"] = "image/svg+xml",
            [".webp"] = "image/webp",
        };

    public static string? Validate(IFormFile file, string type)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedTypes.TryGetValue(extension, out var expectedContentType)
            || !string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return "Formato de imagem não permitido.";
        }

        var normalizedType = type.Trim().ToLowerInvariant();
        if (normalizedType == "logo" && file.Length > 2 * 1024 * 1024)
        {
            return "A logo deve ter no máximo 2 MB.";
        }

        if (normalizedType == "banner" && file.Length > 5 * 1024 * 1024)
        {
            return "O banner deve ter no máximo 5 MB.";
        }

        return null;
    }
}
