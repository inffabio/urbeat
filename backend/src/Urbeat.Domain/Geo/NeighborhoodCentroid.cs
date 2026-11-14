namespace Urbeat.Domain.Geo;

/// <summary>
/// Operational centroid of a neighborhood: the arithmetic mean of the valid address points. The
/// current CNEFE artifact already materializes this value in
/// <c>bairros.latitude_central</c>/<c>longitude_central</c>; the importer copies it and validates
/// it by recomputing the mean. The mean of means (per street or per CEP) is never used.
/// </summary>
public sealed record NeighborhoodCentroid(double Latitude, double Longitude, int PointCount)
{
    /// <summary>Default tolerance for the materialized-vs-recomputed comparison, in metres.</summary>
    public const double DefaultToleranceMeters = 1d;

    /// <summary>Sampling warning threshold. Result is still published below it, flagged as low sample.</summary>
    public const int LowSampleThreshold = 5;

    public static NeighborhoodCentroid? FromPoints(IEnumerable<(double Latitude, double Longitude)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        double latitudeSum = 0d;
        double longitudeSum = 0d;
        var count = 0;

        foreach (var (latitude, longitude) in points)
        {
            if (!GeoDistance.IsValidBrazilCoordinate(latitude, longitude))
            {
                continue;
            }

            latitudeSum += latitude;
            longitudeSum += longitude;
            count++;
        }

        if (count == 0)
        {
            return null;
        }

        return new NeighborhoodCentroid(
            GeoDistance.RoundCoordinate(latitudeSum / count),
            GeoDistance.RoundCoordinate(longitudeSum / count),
            count);
    }

    public bool IsLowSample => PointCount < LowSampleThreshold;

    public double DistanceToMeters(double latitude, double longitude)
    {
        return GeoDistance.HaversineMeters(Latitude, Longitude, latitude, longitude);
    }

    /// <summary>
    /// A materialized centroid diverging from the recomputed mean beyond the tolerance is a
    /// blocking error (<c>COORD_CENTROID_MISMATCH</c>), never a silent correction.
    /// </summary>
    public static bool MatchesMaterialized(
        double materializedLatitude,
        double materializedLongitude,
        NeighborhoodCentroid recomputed,
        double toleranceMeters = DefaultToleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(recomputed);

        if (double.IsNaN(toleranceMeters) || toleranceMeters < 0d)
        {
            return false;
        }

        var distance = GeoDistance.HaversineMeters(
            materializedLatitude,
            materializedLongitude,
            recomputed.Latitude,
            recomputed.Longitude);

        return distance <= toleranceMeters;
    }
}
