namespace Urbeat.Infrastructure.Services.Payments;

public sealed class SystemClock : IMockPixClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
