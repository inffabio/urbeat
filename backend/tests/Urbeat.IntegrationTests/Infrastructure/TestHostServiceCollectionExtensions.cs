using Microsoft.Extensions.DependencyInjection;
using Urbeat.Infrastructure.Outbox;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Service-collection adjustments shared by the integration test hosts.
/// </summary>
internal static class TestHostServiceCollectionExtensions
{
    /// <summary>
    /// Removes the background outbox poller from the host under test.
    /// <para>
    /// <c>AddInfrastructure</c> decides whether to register <see cref="OutboxWorker"/> from the
    /// configuration available while Program registers its services. A factory's
    /// <c>ConfigureAppConfiguration</c> override (for example <c>Outbox:WorkerEnabled=false</c>)
    /// is applied after that point, so it is visible to <c>IOptions&lt;OutboxOptions&gt;</c> but
    /// not to the registration decision. Left in place, the worker keeps polling the fixture's
    /// shared InMemory database in the background and re-fails (or advances) messages while a test
    /// asserts on outbox state, which makes results depend on how long the run takes. Tests drive
    /// the outbox explicitly through <c>DispatchOutboxAsync</c>, so the poller is never needed.
    /// </para>
    /// </summary>
    public static void RemoveBackgroundOutboxWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var descriptors = services
            .Where(descriptor => descriptor.ImplementationType == typeof(OutboxWorker))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
