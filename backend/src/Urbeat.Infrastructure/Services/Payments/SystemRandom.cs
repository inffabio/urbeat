namespace Urbeat.Infrastructure.Services.Payments;

public sealed class SystemRandom : IMockPixRandom
{
    private readonly Random _random = new();

    public int Next(int minimumInclusive, int maximumExclusive)
    {
        return _random.Next(minimumInclusive, maximumExclusive);
    }
}
