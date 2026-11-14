using Urbeat.Infrastructure.Services;
using Xunit;

namespace Urbeat.UnitTests.Infrastructure.Services;

public sealed class GeographicCacheKeyTests
{
    [Fact]
    public void Geo_index_keys_are_versioned_and_normalized()
    {
        Assert.Equal("urbeat:geo:cnefe_2022:neighborhoods", RedisNeighborhoodGeoIndex.BuildIndexKey("CNEFE_2022", null));
        Assert.Equal("urbeat:geo:cnefe_2022:neighborhoods:sp", RedisNeighborhoodGeoIndex.BuildIndexKey("CNEFE_2022", "sp"));
        Assert.Equal("urbeat:geo:cnefe_2022:meta", RedisNeighborhoodGeoIndex.BuildMetaKey("CNEFE_2022", null));
        Assert.Equal("urbeat:geo:unversioned:neighborhoods", RedisNeighborhoodGeoIndex.BuildIndexKey("", null));
    }

    [Fact]
    public void Geo_index_never_writes_to_the_active_key_of_another_version()
    {
        var first = RedisNeighborhoodGeoIndex.BuildIndexKey("v1", "SP");
        var second = RedisNeighborhoodGeoIndex.BuildIndexKey("v2", "SP");

        Assert.NotEqual(first, second);
        Assert.Contains(":v1:", first);
        Assert.Contains(":v2:", second);
    }

    [Fact]
    public void Cep_cache_keys_are_versioned()
    {
        Assert.Equal(
            "urbeat:addr:cnefe-2022:cep:01310100",
            AddressLookupService.BuildCacheKey("CNEFE-2022", "01310100"));

        Assert.Equal(
            "urbeat:addr:unversioned:cep:01310100",
            AddressLookupService.BuildCacheKey(null, "01310100"));
    }
}
