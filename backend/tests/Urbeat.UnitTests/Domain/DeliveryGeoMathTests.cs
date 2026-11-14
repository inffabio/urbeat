using Urbeat.Domain.Geo;
using Xunit;

namespace Urbeat.UnitTests.Domain;

public sealed class DeliveryGeoMathTests
{
    private const double SaoPauloLatitude = -23.5613991;
    private const double SaoPauloLongitude = -46.6558819;

    [Fact]
    public void Haversine_returns_zero_for_the_same_point()
    {
        Assert.Equal(0d, GeoDistance.HaversineKm(SaoPauloLatitude, SaoPauloLongitude, SaoPauloLatitude, SaoPauloLongitude));
    }

    [Fact]
    public void Haversine_matches_one_degree_of_latitude()
    {
        var expected = 6371d * Math.PI / 180d;

        Assert.Equal(expected, GeoDistance.HaversineKm(0d, 0d, 1d, 0d), 6);
    }

    [Fact]
    public void Haversine_is_symmetric()
    {
        var forward = GeoDistance.HaversineKm(-23.5, -46.6, -22.9, -43.2);
        var backward = GeoDistance.HaversineKm(-22.9, -43.2, -23.5, -46.6);

        Assert.Equal(forward, backward, 9);
    }

    [Theory]
    [InlineData(-23.0, -46.0, -23.09, -46.0)] // north
    [InlineData(-23.0, -46.0, -22.91, -46.0)] // south
    [InlineData(-23.0, -46.0, -23.0, -45.9)] // east
    [InlineData(-23.0, -46.0, -23.0, -46.1)] // west
    public void Haversine_covers_the_full_circle(double originLatitude, double originLongitude, double latitude, double longitude)
    {
        var distance = GeoDistance.HaversineKm(originLatitude, originLongitude, latitude, longitude);

        Assert.True(distance > 9.5 && distance < 10.5, $"Unexpected distance: {distance}");
    }

    [Fact]
    public void Eligibility_includes_the_exact_boundary()
    {
        Assert.True(GeoDistance.IsWithinRadius(10d, 10d));
        Assert.True(GeoDistance.IsWithinRadius(9.999999999d, 10d));
        Assert.False(GeoDistance.IsWithinRadius(10.0000001d, 10d));
    }

    [Fact]
    public void Eligibility_never_applies_a_circuity_factor()
    {
        // A 10 km straight-line distance is not multiplied by 1.3 to become 13 km.
        Assert.True(GeoDistance.IsWithinRadius(10d, 10d));
    }

    [Theory]
    [InlineData(double.NaN, -46.6)]
    [InlineData(-23.5, double.PositiveInfinity)]
    [InlineData(0d, 0d)]
    [InlineData(95d, -46.6)]
    [InlineData(-23.5, 200d)]
    [InlineData(10d, -20d)] // outside the generous Brazil envelope
    public void Invalid_coordinates_are_rejected(double latitude, double longitude)
    {
        Assert.False(GeoDistance.IsValidBrazilCoordinate(latitude, longitude));
    }

    [Fact]
    public void Oceanic_islands_are_inside_the_brazil_envelope()
    {
        Assert.True(GeoDistance.IsValidBrazilCoordinate(-3.8496, -32.4274)); // Fernando de Noronha
        Assert.True(GeoDistance.IsValidBrazilCoordinate(-20.5, -29.3)); // Trindade
        Assert.True(GeoDistance.IsValidBrazilCoordinate(-33.7, -53.4)); // Chuí
    }

    [Fact]
    public void Round_coordinate_keeps_at_most_seven_decimals()
    {
        var rounded = GeoDistance.RoundCoordinate(-23.56139912345);

        Assert.Equal(-23.5613991, rounded);
    }

    [Fact]
    public void Centroid_is_the_arithmetic_mean_of_the_points()
    {
        var centroid = NeighborhoodCentroid.FromPoints(
        [
            (-23.5, -46.6),
            (-23.6, -46.7),
            (-23.4, -46.5)
        ]);

        Assert.NotNull(centroid);
        Assert.Equal(-23.5, centroid!.Latitude, 9);
        Assert.Equal(-46.6, centroid.Longitude, 9);
        Assert.Equal(3, centroid.PointCount);
    }

    [Fact]
    public void Centroid_is_order_independent()
    {
        var first = NeighborhoodCentroid.FromPoints([(-23.5, -46.6), (-23.7, -46.8)]);
        var second = NeighborhoodCentroid.FromPoints([(-23.7, -46.8), (-23.5, -46.6)]);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Latitude, second!.Latitude, 12);
        Assert.Equal(first.Longitude, second.Longitude, 12);
    }

    [Fact]
    public void Centroid_is_null_without_valid_points()
    {
        Assert.Null(NeighborhoodCentroid.FromPoints([]));
        Assert.Null(NeighborhoodCentroid.FromPoints([(double.NaN, 10d)]));
    }

    [Fact]
    public void Materialized_centroid_is_validated_against_the_recomputed_mean()
    {
        var centroid = NeighborhoodCentroid.FromPoints([(-23.5, -46.6), (-23.5000001, -46.6)]);

        Assert.NotNull(centroid);
        Assert.True(NeighborhoodCentroid.MatchesMaterialized(-23.5, -46.6, centroid!, toleranceMeters: 1d));

        var distant = NeighborhoodCentroid.FromPoints([(-23.6, -46.6), (-23.6, -46.6)]);
        Assert.False(NeighborhoodCentroid.MatchesMaterialized(-23.5, -46.6, distant!, toleranceMeters: 1d));
    }
}
