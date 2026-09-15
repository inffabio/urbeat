namespace Urbeat.Infrastructure.Services.Payments;

public interface IMockPixClock
{
    DateTime UtcNow { get; }
}
