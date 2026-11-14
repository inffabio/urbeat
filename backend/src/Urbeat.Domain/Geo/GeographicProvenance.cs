namespace Urbeat.Domain.Geo;

/// <summary>
/// Canonical vocabulary used by the geographic import pipeline and the delivery area migration.
/// These values are persisted as strings and are part of the data contract, so they must never be
/// renamed without a migration.
/// </summary>
public static class GeographicProvenance
{
    public static class NeighborhoodKind
    {
        public const string OfficialCnefe = "official_cnefe";
        public const string Manual = "manual";
        public const string Legacy = "legacy";
    }

    public static class CoordinateMethod
    {
        /// <summary>
        /// Arithmetic mean of the valid address points of the neighborhood. The previous
        /// <c>cnefe_point_centroid_v1</c> value is discontinued for neighborhoods.
        /// </summary>
        public const string AddressPointMean = "ADDRESS_POINT_MEAN";

        public const string Haversine = "haversine";
    }

    public static class CoordinateStatus
    {
        public const string Computed = "computed";
        public const string Pending = "pending";
        public const string Manual = "manual";
    }

    public static class RepresentativeMethod
    {
        public const string NearestToCentroid = "nearest_to_centroid_v1";
        public const string MedoidSampled = "medoid_sampled_v1";
    }

    public static class Source
    {
        public const string Cnefe = "cnefe";
        public const string IbgeLocalidades = "ibge_localidades";
        public const string OpenStreetMap = "openstreetmap";
    }

    public static class ImportStatus
    {
        public const string Discovered = "discovered";
        public const string Staging = "staging";
        public const string Validating = "validating";
        public const string Publishing = "publishing";
        public const string Published = "published";
        public const string Failed = "failed";
        public const string RolledBack = "rolled_back";
    }

    public static class LocalityType
    {
        public const string UrbanNeighborhood = "urban_neighborhood";
        public const string IndigenousVillage = "indigenous_village";
        public const string RuralLocality = "rural_locality";
        public const string Other = "other";
        public const string Unknown = "unknown";
    }

    /// <summary>
    /// Error codes required by the CNEFE import specification. They are also written to
    /// <c>ImportErrors.Code</c>, so they are stable identifiers.
    /// </summary>
    public static class ImportErrorCode
    {
        public const string InvalidUf = "INVALID_UF";
        public const string InvalidIbge = "INVALID_IBGE";
        public const string UfIbgeMismatch = "UF_IBGE_MISMATCH";
        public const string MissingStreet = "MISSING_STREET";
        public const string MissingCoord = "MISSING_COORD";
        public const string InvalidCoord = "INVALID_COORD";
        public const string CoordOutOfCity = "COORD_OUT_OF_CITY";
        public const string InvalidZip = "INVALID_ZIP";
        public const string PendingNeighborhood = "PENDING_NEIGHBORHOOD";
        public const string UnknownCity = "UNKNOWN_CITY";
        public const string DuplicateRow = "DUPLICATE_ROW";
        public const string LowSample = "LOW_SAMPLE";
        public const string CoordCentroidMismatch = "COORD_CENTROID_MISMATCH";
    }
}
