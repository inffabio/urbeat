using Urbeat.Domain.Geo;

namespace Urbeat.Infrastructure.Helpers;

/// <summary>
/// Compatibility facade over <see cref="NeighborhoodName.Normalize"/>. The canonical form now
/// replaces punctuation with a space and collapses whitespace in addition to folding accents and
/// lowercasing. Existing rows are renormalized by the geographic base migration; new code must use
/// the domain helper directly.
/// </summary>
public static class NeighborhoodNormalizer
{
    public static string Normalize(string? value)
    {
        return NeighborhoodName.Normalize(value);
    }
}
