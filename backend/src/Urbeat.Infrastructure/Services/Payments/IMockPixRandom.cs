namespace Urbeat.Infrastructure.Services.Payments;

public interface IMockPixRandom
{
    int Next(int minimumInclusive, int maximumExclusive);
}
