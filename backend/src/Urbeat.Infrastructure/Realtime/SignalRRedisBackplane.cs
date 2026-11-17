using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Urbeat.Infrastructure.Realtime;

/// <summary>
/// Decides whether the SignalR Redis backplane should be registered. The decision is isolated so it
/// can be unit-tested without starting a Redis server or building the full web host. It is shared by
/// the WebApi host and the outbox worker so both sides of the backplane are enabled together.
/// </summary>
public static class SignalRRedisBackplane
{
    public const string ConnectionStringKey = "Redis:ConnectionString";
    public const string EnabledKey = "SignalR:RedisBackplaneEnabled";

    /// <summary>
    /// The backplane is enabled only when a Redis connection string is configured <em>and</em> the
    /// operator explicitly opts in with <c>SignalR:RedisBackplaneEnabled=true</c>. A missing flag
    /// never silently enables the backplane on a default/localhost connection string. The "Testing"
    /// environment always falls back to the in-process backplane so that the InMemory test harness
    /// never attempts a real Redis connection.
    /// </summary>
    public static bool ShouldEnable(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringKey]))
        {
            return false;
        }

        return configuration.GetValue<bool?>(EnabledKey) == true;
    }
}
