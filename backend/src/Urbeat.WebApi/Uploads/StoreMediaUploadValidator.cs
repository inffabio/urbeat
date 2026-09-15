using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;

namespace Urbeat.WebApi.Uploads;

public static class StoreMediaUploadValidator
{
    public const long LogoMaxBytes = 2L * 1024 * 1024;
    public const long BannerMaxBytes = 5L * 1024 * 1024;
    public const long ProductMaxBytes = 6L * 1024 * 1024;

    public const string InvalidUploadTypeMessage = "Tipo de mídia inválido.";

    private const string InvalidFormatMessage = "Formato de imagem não permitido.";
    private const string InvalidContentMessage = "O arquivo de imagem é inválido ou está corrompido.";

    // Canonical upload categories accepted by the API. The value is used both to
    // select the size limit and to build the Cloudinary folder, so anything else
    // must be rejected before it can be interpolated into a path.
    public static readonly IReadOnlySet<string> SupportedUploadTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "logo",
            "banner",
            "products",
            "store-media",
        };

    private static readonly IReadOnlyDictionary<string, string[]> AllowedTypes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".avif"] = new[] { "image/avif" },
            [".jpg"] = new[] { "image/jpeg", "image/jpg" },
            [".jpeg"] = new[] { "image/jpeg", "image/jpg" },
            [".png"] = new[] { "image/png" },
            [".svg"] = new[] { "image/svg+xml" },
            [".webp"] = new[] { "image/webp" },
        };

    // Active/embedding content that is never part of the safe SVG profile.
    private static readonly HashSet<string> UnsafeSvgElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "foreignObject",
        "iframe",
        "embed",
        "object",
        "applet",
        "frame",
        "frameset",
        "link",
        "meta",
        "base",
        "handler",
        "listener",
    };

    // Attributes that load or point at another resource. Only same-document
    // fragment references (#id) are permitted.
    private static readonly HashSet<string> ExternalReferenceAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href",
        "src",
        "poster",
        "data",
        "action",
        "formaction",
    };

    // Attributes whose value can carry a CSS url(...) reference.
    private static readonly HashSet<string> CssUrlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "style",
        "fill",
        "stroke",
        "filter",
        "clip-path",
        "mask",
        "marker-start",
        "marker-mid",
        "marker-end",
        "cursor",
        "background",
    };

    public static bool IsSupportedUploadType(string? type)
        => !string.IsNullOrWhiteSpace(type) && SupportedUploadTypes.Contains(type.Trim());

    public static string? Validate(IFormFile file, string type)
    {
        if (!IsSupportedUploadType(type))
        {
            return InvalidUploadTypeMessage;
        }

        var normalizedType = type.Trim().ToLowerInvariant();

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension)
            || !AllowedTypes.TryGetValue(extension, out var allowedContentTypes))
        {
            return InvalidFormatMessage;
        }

        if (!IsAllowedContentType(allowedContentTypes, file.ContentType))
        {
            return InvalidFormatMessage;
        }

        var sizeError = ValidateSize(file.Length, normalizedType);
        if (sizeError is not null)
        {
            return sizeError;
        }

        return ValidateContent(file, extension.ToLowerInvariant());
    }

    private static bool IsAllowedContentType(IEnumerable<string> allowedContentTypes, string? contentType)
    {
        // A missing/empty content type is accepted because content sniffing is authoritative.
        // Browsers upload MIME-less files as application/octet-stream, which the frontend
        // also treats as unknown, so it must not be rejected here either.
        // When the client does send a specific type it must exactly match the extension.
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var normalized = contentType.Trim();
        if (normalized.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return allowedContentTypes.Any(allowed =>
            string.Equals(allowed, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ValidateSize(long length, string? type)
    {
        var normalizedType = type?.Trim().ToLowerInvariant() ?? string.Empty;

        return normalizedType switch
        {
            "logo" when length > LogoMaxBytes => "A logo deve ter no máximo 2 MB.",
            "banner" when length > BannerMaxBytes => "O banner deve ter no máximo 5 MB.",
            _ when length > ProductMaxBytes => "A imagem do produto deve ter no máximo 6 MB.",
            _ => null,
        };
    }

    private static string? ValidateContent(IFormFile file, string extension)
    {
        byte[] content;
        try
        {
            content = ReadAllBytes(file);
        }
        catch
        {
            return InvalidContentMessage;
        }

        var isValid = extension switch
        {
            ".png" => IsPng(content),
            ".jpg" or ".jpeg" => IsJpeg(content),
            ".webp" => IsWebp(content),
            ".avif" => IsAvif(content),
            ".svg" => IsSvg(content),
            _ => false,
        };

        return isValid ? null : InvalidContentMessage;
    }

    private static byte[] ReadAllBytes(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        long total = 0;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > ProductMaxBytes)
            {
                throw new InvalidOperationException("Upload exceeds the maximum supported size.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsPng(byte[] content)
    {
        if (content.Length < 8
            || content[0] != 0x89
            || content[1] != 0x50
            || content[2] != 0x4E
            || content[3] != 0x47
            || content[4] != 0x0D
            || content[5] != 0x0A
            || content[6] != 0x1A
            || content[7] != 0x0A)
        {
            return false;
        }

        // Walk the chunk list, verifying chunk framing and CRC. IEND must be the
        // final chunk so trailing/padded bytes after it are rejected.
        var offset = 8;
        var sawHeader = false;
        var sawData = false;
        var sawEnd = false;
        while (offset + 12 <= content.Length)
        {
            var chunkLength = ReadBigEndianInt32(content, offset);
            if (chunkLength < 0)
            {
                return false;
            }

            var chunkEnd = (long)offset + 12 + chunkLength;
            if (chunkEnd > content.Length)
            {
                return false;
            }

            var chunkType = Encoding.ASCII.GetString(content, offset + 4, 4);
            if (!IsValidPngChunkType(chunkType))
            {
                return false;
            }

            var storedCrc = ReadBigEndianUInt32(content, offset + 8 + chunkLength);
            if (storedCrc != ComputeCrc32(content, offset + 4, 4 + chunkLength))
            {
                return false;
            }

            if (!sawHeader)
            {
                if (chunkType != "IHDR" || chunkLength != 13)
                {
                    return false;
                }

                sawHeader = true;
            }
            else if (chunkType == "IHDR")
            {
                return false;
            }
            else if (chunkType == "IEND")
            {
                if (chunkLength != 0)
                {
                    return false;
                }

                sawEnd = true;
                offset = (int)chunkEnd;
                break;
            }
            else if (chunkType == "IDAT")
            {
                sawData = true;
            }

            offset = (int)chunkEnd;
        }

        return sawHeader && sawData && sawEnd && offset == content.Length && TryIdentify(content);
    }

    private static bool IsValidPngChunkType(string chunkType)
    {
        if (chunkType.Length != 4)
        {
            return false;
        }

        foreach (var value in chunkType)
        {
            if (!((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z')))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    private static uint ComputeCrc32(byte[] content, int offset, int length)
    {
        var crc = 0xFFFFFFFFu;
        for (var index = 0; index < length; index++)
        {
            crc = Crc32Table[(crc ^ content[offset + index]) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static bool IsJpeg(byte[] content)
    {
        if (content.Length < 4
            || content[0] != 0xFF
            || content[1] != 0xD8)
        {
            return false;
        }

        // A complete JPEG stream ends with the EOI marker; header-only or truncated
        // payloads do not.
        if (content[^2] != 0xFF || content[^1] != 0xD9)
        {
            return false;
        }

        return TryIdentify(content);
    }

    private static bool IsWebp(byte[] content)
    {
        if (content.Length < 16
            || content[0] != (byte)'R'
            || content[1] != (byte)'I'
            || content[2] != (byte)'F'
            || content[3] != (byte)'F'
            || content[8] != (byte)'W'
            || content[9] != (byte)'E'
            || content[10] != (byte)'B'
            || content[11] != (byte)'P')
        {
            return false;
        }

        var declaredSize = ReadLittleEndianInt32(content, 4);
        if (declaredSize < 0 || declaredSize + 8 != content.Length)
        {
            return false;
        }

        var chunkType = Encoding.ASCII.GetString(content, 12, 4);
        if (chunkType is not ("VP8 " or "VP8L" or "VP8X"))
        {
            return false;
        }

        return TryIdentify(content);
    }

    private static bool IsAvif(byte[] content)
    {
        // ImageSharp 3.1.12 ships no AVIF decoder. No compatible decoder could be
        // safely added: HeyRed.ImageSharp.Heif requires native libheif binaries and
        // NeoSolve.ImageSharp.AVIF shells out to avifenc/avifdec, both of which add
        // RID-specific native/process dependencies the OCI deployment does not
        // guarantee. AVIF is therefore validated by parsing the ISOBMFF/HEIF
        // structure: a still-image `meta` box with a picture handler, primary item,
        // item location and an `av01` item, plus non-empty coded data (`mdat`/`idat`)
        // whose extents point inside a real media payload box. This rejects
        // header-only, empty-mdat, fabricated-metadata and truncated files but is
        // not a full pixel decode.
        if (!TryReadBoxHeader(content, 0, content.Length, out var ftypType, out var ftypHeaderSize, out var ftypSize)
            || ftypType != "ftyp"
            || !HasAvifBrand(content, ftypHeaderSize, (int)ftypSize))
        {
            return false;
        }

        var sawMeta = false;
        var metaStart = 0;
        var metaEnd = 0;
        var mediaRanges = new List<MediaRange>();
        var offset = 0;
        while (offset < content.Length)
        {
            if (!TryReadBoxHeader(content, offset, content.Length, out var boxType, out var headerSize, out var boxSize))
            {
                return false;
            }

            if (boxType == "meta")
            {
                sawMeta = true;
                metaStart = offset + headerSize;
                metaEnd = offset + (int)boxSize;
            }
            else if (boxType == "mdat")
            {
                var payloadStart = offset + headerSize;
                var payloadEnd = offset + (int)boxSize;
                if (payloadEnd > payloadStart)
                {
                    mediaRanges.Add(new MediaRange(payloadStart, payloadEnd, IsIdat: false));
                }
            }

            offset += (int)boxSize;
        }

        if (offset != content.Length || !sawMeta)
        {
            return false;
        }

        if (!ValidateAvifMeta(content, metaStart, metaEnd, mediaRanges))
        {
            return false;
        }

        return mediaRanges.Count > 0;
    }

    // Reads an ISO BMFF box header (32-bit size, 64-bit extended size when the size
    // field is 1, or "to end of parent" when it is 0) and validates that the box
    // stays within the given bounds.
    private static bool TryReadBoxHeader(
        byte[] content,
        int offset,
        int end,
        out string type,
        out int headerSize,
        out long boxSize)
    {
        type = string.Empty;
        headerSize = 0;
        boxSize = 0;

        if (offset < 0 || offset + 8 > end)
        {
            return false;
        }

        boxSize = ReadBigEndianUInt32(content, offset);
        type = Encoding.ASCII.GetString(content, offset + 4, 4);
        headerSize = 8;

        if (boxSize == 1)
        {
            if (offset + 16 > end)
            {
                return false;
            }

            boxSize = ReadBigEndianInt64(content, offset + 8);
            headerSize = 16;
        }
        else if (boxSize == 0)
        {
            boxSize = end - offset;
        }

        // Compare against the remaining span instead of `offset + boxSize` so a
        // 64-bit extended size near long.MaxValue cannot overflow into a passing
        // bounds check. `end - offset` is non-negative because the caller has
        // already guaranteed `offset + 8 <= end`.
        if (boxSize < headerSize || boxSize > end - offset)
        {
            return false;
        }

        return true;
    }

    private static bool ValidateAvifMeta(
        byte[] content,
        int metaStart,
        int metaEnd,
        List<MediaRange> mediaRanges)
    {
        if (metaStart + 4 > metaEnd)
        {
            return false;
        }

        var sawHandler = false;
        var sawPrimaryItem = false;
        var sawItemLocation = false;
        var sawItemInfo = false;
        var sawAv1Item = false;
        var primaryItemId = 0L;
        var itemLocationStart = 0;
        var itemLocationEnd = 0;
        var idatStart = 0;
        var idatEnd = 0;
        var hasIdat = false;

        var offset = metaStart + 4;
        while (offset < metaEnd)
        {
            if (!TryReadBoxHeader(content, offset, metaEnd, out var boxType, out var headerSize, out var boxSize))
            {
                return false;
            }

            var payloadStart = offset + headerSize;
            var payloadEnd = offset + (int)boxSize;

            switch (boxType)
            {
                case "hdlr":
                    sawHandler = IsPictureHandler(content, payloadStart, payloadEnd);
                    break;
                case "pitm":
                    sawPrimaryItem = TryReadPrimaryItemId(content, payloadStart, payloadEnd, out primaryItemId);
                    break;
                case "iloc":
                    sawItemLocation = true;
                    itemLocationStart = payloadStart;
                    itemLocationEnd = payloadEnd;
                    break;
                case "iinf":
                    sawItemInfo = true;
                    sawAv1Item = ContainsAscii(content, payloadStart, payloadEnd, "av01");
                    break;
                case "idat":
                    if (payloadEnd > payloadStart)
                    {
                        hasIdat = true;
                        idatStart = payloadStart;
                        idatEnd = payloadEnd;
                    }
                    break;
            }

            offset += (int)boxSize;
        }

        if (offset != metaEnd
            || !sawHandler
            || !sawPrimaryItem
            || !sawItemInfo
            || !sawAv1Item)
        {
            return false;
        }

        // `idat` can appear after `iloc`, so record its payload range before
        // validating extents against the complete set of media payload boxes.
        if (hasIdat)
        {
            mediaRanges.Add(new MediaRange(idatStart, idatEnd, IsIdat: true));
        }

        return sawItemLocation
            && ValidateItemLocation(content, itemLocationStart, itemLocationEnd, primaryItemId, mediaRanges);
    }

    // Reads the primary item_ID from a `pitm` box: 16 bits for version 0 and 32
    // bits for version 1.
    private static bool TryReadPrimaryItemId(byte[] content, int start, int end, out long itemId)
    {
        itemId = 0;
        if (start + 4 > end)
        {
            return false;
        }

        var version = content[start];
        if (version == 0)
        {
            if (start + 6 > end)
            {
                return false;
            }

            itemId = ReadBigEndianUInt16(content, start + 4);
            return true;
        }

        if (start + 8 > end)
        {
            return false;
        }

        itemId = ReadBigEndianUInt32(content, start + 4);
        return true;
    }

    private static bool IsPictureHandler(byte[] content, int start, int end)
        => start + 12 <= end
            && content[start + 8] == (byte)'p'
            && content[start + 9] == (byte)'i'
            && content[start + 10] == (byte)'c'
            && content[start + 11] == (byte)'t';

    private static bool ValidateItemLocation(
        byte[] content,
        int start,
        int end,
        long primaryItemId,
        List<MediaRange> mediaRanges)
    {
        if (start + 4 > end)
        {
            return false;
        }

        var version = content[start];
        var position = start + 4;

        if (position + 2 > end)
        {
            return false;
        }

        var sizes = content[position];
        var offsetSize = (sizes >> 4) & 0x0F;
        var lengthSize = sizes & 0x0F;
        var baseOffsetSize = (content[position + 1] >> 4) & 0x0F;
        var indexSize = version < 1 ? 0 : content[position + 1] & 0x0F;
        position += 2;

        if (position + 2 > end)
        {
            return false;
        }

        var itemCount = ReadBigEndianUInt16(content, position);
        position += 2;

        var sawExtent = false;
        for (var item = 0; item < itemCount; item++)
        {
            // `iloc` version 0/1 stores a 16-bit item_ID; version 2 widens it to 32
            // bits, so the primary-item association must be read with the same width
            // the writer used or a valid version-2 file would be misparsed.
            long itemId;
            if (version < 2)
            {
                if (position + 2 > end)
                {
                    return false;
                }

                itemId = ReadBigEndianUInt16(content, position);
                position += 2;
            }
            else
            {
                if (position + 4 > end)
                {
                    return false;
                }

                itemId = ReadBigEndianUInt32(content, position);
                position += 4;
            }

            var isPrimaryItem = itemId == primaryItemId;

            var constructionMethod = 0;
            if (version >= 1)
            {
                if (position + 2 > end)
                {
                    return false;
                }

                // Version 1 packs 12 reserved bits before the 4-bit method.
                constructionMethod = ReadBigEndianUInt16(content, position) & 0x0F;
                position += 2;
            }

            if (position + 2 > end)
            {
                return false;
            }

            position += 2; // data_reference_index

            var baseOffset = ReadSizedValue(content, position, baseOffsetSize, end);
            if (baseOffset < 0)
            {
                return false;
            }

            position += baseOffsetSize;

            if (position + 2 > end)
            {
                return false;
            }

            var extentCount = ReadBigEndianUInt16(content, position);
            position += 2;

            for (var extent = 0; extent < extentCount; extent++)
            {
                var extentOffset = ReadSizedValue(content, position, offsetSize, end);
                if (extentOffset < 0)
                {
                    return false;
                }

                position += offsetSize;

                var extentLength = ReadSizedValue(content, position, lengthSize, end);
                if (extentLength < 0)
                {
                    return false;
                }

                position += lengthSize;

                if (indexSize > 0)
                {
                    var extentIndex = ReadSizedValue(content, position, indexSize, end);
                    if (extentIndex < 0)
                    {
                        return false;
                    }

                    position += indexSize;
                }

                if (isPrimaryItem
                    && ExtentWithinMediaPayload(baseOffset, extentOffset, extentLength, constructionMethod, mediaRanges))
                {
                    sawExtent = true;
                }
            }
        }

        return sawExtent;
    }

    private readonly record struct MediaRange(long Start, long End, bool IsIdat);

    // An extent is only meaningful when it addresses coded image bytes, so it must
    // be fully contained in a media payload box (a file-level `mdat` or `meta`'s
    // `idat`), not merely inside the file. All arithmetic is overflow-checked
    // because the offset/length fields are attacker-controlled 64-bit values.
    private static bool ExtentWithinMediaPayload(
        long baseOffset,
        long extentOffset,
        long extentLength,
        int constructionMethod,
        List<MediaRange> mediaRanges)
    {
        // Method 0 is file-relative, method 1 is relative to the `idat` payload,
        // and method 2 addresses other items rather than coded media bytes.
        if (extentLength <= 0 || constructionMethod > 1)
        {
            return false;
        }

        foreach (var range in mediaRanges)
        {
            if (range.IsIdat != (constructionMethod == 1))
            {
                continue;
            }

            long start;
            if (constructionMethod == 0)
            {
                if (!TryAddNonNegative(baseOffset, extentOffset, out start))
                {
                    return false;
                }
            }
            else if (!TryAddNonNegative(range.Start, baseOffset, out var relativeStart)
                || !TryAddNonNegative(relativeStart, extentOffset, out start))
            {
                continue;
            }

            if (start >= range.Start && start <= range.End && extentLength <= range.End - start)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAddNonNegative(long left, long right, out long result)
    {
        if (left < 0 || right < 0 || left > long.MaxValue - right)
        {
            result = 0;
            return false;
        }

        result = left + right;
        return true;
    }

    private static long ReadSizedValue(byte[] content, int offset, int size, int end)
    {
        if (size == 0)
        {
            return 0;
        }

        if (offset + size > end)
        {
            return -1;
        }

        return size switch
        {
            4 => ReadBigEndianUInt32(content, offset),
            8 => ReadBigEndianInt64(content, offset),
            _ => -1,
        };
    }

    private static bool ContainsAscii(byte[] content, int start, int end, string value)
    {
        var length = value.Length;
        if (length == 0 || start + length > end)
        {
            return false;
        }

        for (var index = start; index + length <= end; index++)
        {
            var match = true;
            for (var offset = 0; offset < length; offset++)
            {
                if (content[index + offset] != value[offset])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAvifBrand(byte[] content, int headerSize, int boxEnd)
    {
        var majorBrandOffset = headerSize;
        if (majorBrandOffset + 4 > boxEnd)
        {
            return false;
        }

        if (IsAvifBrand(content, majorBrandOffset))
        {
            return true;
        }

        for (var offset = majorBrandOffset + 8; offset + 4 <= boxEnd; offset += 4)
        {
            if (IsAvifBrand(content, offset))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAvifBrand(byte[] content, int offset)
        => offset + 4 <= content.Length
            && content[offset] == (byte)'a'
            && content[offset + 1] == (byte)'v'
            && content[offset + 2] == (byte)'i'
            && (content[offset + 3] == (byte)'f' || content[offset + 3] == (byte)'s');

    private static bool IsSvg(byte[] content)
    {
        if (content.Length == 0 || content.Any(value => value == 0))
        {
            return false;
        }

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = Math.Max(content.Length, 1) * 8L,
            };

            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch
        {
            return false;
        }

        // XDocument enforces a single root element; it must be an <svg> element.
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var node in document.Nodes())
        {
            if (node is XProcessingInstruction instruction
                && instruction.Target.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            if (UnsafeSvgElements.Contains(element.Name.LocalName))
            {
                return false;
            }

            if (element.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)
                && ContainsDangerousCss(element.Value))
            {
                return false;
            }

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                var name = attribute.Name.LocalName;
                var value = attribute.Value;

                // Event handlers (onload, onclick, ...) are active content.
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (HasDangerousScheme(value))
                {
                    return false;
                }

                if (ExternalReferenceAttributes.Contains(name) && IsExternalReference(value))
                {
                    return false;
                }

                if (CssUrlAttributes.Contains(name) && ContainsDangerousCss(value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasDangerousScheme(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        // Strip whitespace/control characters so "java\tscript:" style obfuscation
        // cannot bypass the scheme check.
        var compact = RemoveWhitespaceAndControl(value).ToLowerInvariant();
        return compact.Contains("javascript:", StringComparison.Ordinal)
            || compact.Contains("vbscript:", StringComparison.Ordinal);
    }

    private static string RemoveWhitespaceAndControl(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character) && !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsExternalReference(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        // Only same-document fragment references are allowed; http(s):, file:,
        // data: and protocol-relative references are all rejected.
        return !trimmed.StartsWith('#');
    }

    private static bool ContainsDangerousCss(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var compact = RemoveWhitespaceAndControl(value).ToLowerInvariant();
        if (compact.Contains("javascript:", StringComparison.Ordinal)
            || compact.Contains("vbscript:", StringComparison.Ordinal)
            || compact.Contains("expression(", StringComparison.Ordinal)
            || compact.Contains("-moz-binding", StringComparison.Ordinal)
            || compact.Contains("behavior:", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Contains("@import", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsExternalUrl(value);
    }

    private static bool ContainsExternalUrl(string value)
    {
        var index = 0;
        while ((index = value.IndexOf("url(", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var rest = value[(index + 4)..].TrimStart();
            if (rest.StartsWith('"') || rest.StartsWith('\''))
            {
                rest = rest[1..].TrimStart();
            }

            if (!rest.StartsWith('#'))
            {
                return true;
            }

            index += 4;
        }

        return false;
    }

    private static bool TryIdentify(byte[] content)
    {
        try
        {
            var info = Image.Identify(content.AsSpan());
            return info is not null && info.Width > 0 && info.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadBigEndianInt32(byte[] content, int offset)
        => (content[offset] << 24)
            | (content[offset + 1] << 16)
            | (content[offset + 2] << 8)
            | content[offset + 3];

    private static int ReadBigEndianUInt16(byte[] content, int offset)
        => (content[offset] << 8) | content[offset + 1];

    private static uint ReadBigEndianUInt32(byte[] content, int offset)
        => ((uint)content[offset] << 24)
            | ((uint)content[offset + 1] << 16)
            | ((uint)content[offset + 2] << 8)
            | content[offset + 3];

    private static long ReadBigEndianInt64(byte[] content, int offset)
        => ((long)ReadBigEndianUInt32(content, offset) << 32) | ReadBigEndianUInt32(content, offset + 4);

    private static int ReadLittleEndianInt32(byte[] content, int offset)
        => content[offset]
            | (content[offset + 1] << 8)
            | (content[offset + 2] << 16)
            | (content[offset + 3] << 24);
}
