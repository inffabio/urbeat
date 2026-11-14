namespace Urbeat.Domain.Geo;

/// <summary>
/// Straight-line (great circle) distance helpers shared by the delivery area rules, the
/// neighborhood import pipeline and the API projections. Route distance, circuity factors and
/// travel time estimates are intentionally out of scope: eligibility is always
/// <c>haversine_km &lt;= radius_km</c> over the complete circle.
/// </summary>
public static class GeoDistance
{
    /// <summary>Mean Earth radius in kilometres, matching the historical Urbeat calculation.</summary>
    public const double EarthRadiusKm = 6371d;

    /// <summary>
    /// Boundary tolerance used when comparing an eligibility distance with the configured radius.
    /// It absorbs floating point noise at the exact boundary without widening the circle.
    /// </summary>
    public const double RadiusEpsilonKm = 1e-9;

    // Generous envelope covering the Brazilian mainland and the oceanic islands used by the
    // CNEFE artifact (Fernando de Noronha, Trindade, Martin Vaz). It is deliberately wider than
    // the official territory so that a future artifact is not rejected for a coastal coordinate.
    public const double BrazilMinLatitude = -34.5d;
    public const double BrazilMaxLatitude = 6.0d;
    public const double BrazilMinLongitude = -74.5d;
    public const double BrazilMaxLongitude = -28.0d;

    public static double HaversineKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var phi1 = latitude1 * Math.PI / 180d;
        var phi2 = latitude2 * Math.PI / 180d;
        var deltaPhi = (latitude2 - latitude1) * Math.PI / 180d;
        var deltaLambda = (longitude2 - longitude1) * Math.PI / 180d;

        var sinPhi = Math.Sin(deltaPhi / 2d);
        var sinLambda = Math.Sin(deltaLambda / 2d);
        var haversine = (sinPhi * sinPhi) + (Math.Cos(phi1) * Math.Cos(phi2) * sinLambda * sinLambda);
        haversine = Math.Clamp(haversine, 0d, 1d);

        return 2d * EarthRadiusKm * Math.Asin(Math.Sqrt(haversine));
    }

    public static double HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        return HaversineKm(latitude1, longitude1, latitude2, longitude2) * 1000d;
    }

    /// <summary>Distance in kilometres rounded for display only. Never use the rounded value for eligibility.</summary>
    public static double RoundForDisplay(double distanceKm, int decimals = 2)
    {
        return Math.Round(distanceKm, decimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Eligibility check for a complete circle. No 1.3 factor, no directional box and no route
    /// estimate is applied anywhere in the system.
    /// </summary>
    public static bool IsWithinRadius(double distanceKm, double radiusKm)
    {
        if (!double.IsFinite(distanceKm) || !double.IsFinite(radiusKm) || radiusKm <= 0d)
        {
            return false;
        }

        return distanceKm <= radiusKm + RadiusEpsilonKm;
    }

    public static bool IsValidBrazilCoordinate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            return false;
        }

        if (latitude is < -90d or > 90d || longitude is < -180d or > 180d)
        {
            return false;
        }

        // (0,0) is the classic "no data" placeholder and is never a valid Brazilian address.
        if (latitude == 0d && longitude == 0d)
        {
            return false;
        }

        return latitude is >= BrazilMinLatitude and <= BrazilMaxLatitude
            && longitude is >= BrazilMinLongitude and <= BrazilMaxLongitude;
    }

    /// <summary>Coordinates are stored with at most seven decimals, keeping <c>double precision</c>.</summary>
    public static double RoundCoordinate(double value)
    {
        return double.IsFinite(value) ? Math.Round(value, 7, MidpointRounding.AwayFromZero) : value;
    }

    /// <summary>
    /// Cheap bounding-box prefilter used to avoid a full table scan before the Haversine
    /// calculation in the PostgreSQL fallback path.
    /// </summary>
    public static (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) BoundingBox(
        double latitude,
        double longitude,
        double radiusKm)
    {
        var safeRadius = double.IsFinite(radiusKm) && radiusKm > 0d ? radiusKm : 0d;

        // The box is only a prefilter: it is deliberately a little larger than the circle
        // (1% plus 0.5 km) so a point exactly on the boundary is never discarded before the
        // authoritative Haversine test.
        var paddedRadiusKm = (safeRadius * 1.01d) + 0.5d;
        var latitudeDelta = paddedRadiusKm / 111.32d;
        var cosLatitude = Math.Cos(latitude * Math.PI / 180d);
        var longitudeDelta = Math.Abs(cosLatitude) < 1e-6d
            ? 180d
            : paddedRadiusKm / (111.32d * Math.Abs(cosLatitude));

        return (
            Math.Max(-90d, latitude - latitudeDelta),
            Math.Min(90d, latitude + latitudeDelta),
            Math.Max(-180d, longitude - longitudeDelta),
            Math.Min(180d, longitude + longitudeDelta));
    }
}
