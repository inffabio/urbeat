using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Urbeat.Infrastructure.Outbox;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Guards the isolation contract of <see cref="TestWebApplicationFactory"/>. Program registers the
/// outbox poller before a factory's configuration overrides are merged, so removing the hosted
/// worker is what keeps the fixture database free of background writes between assertions.
/// </summary>
public sealed class TestWebApplicationFactoryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TestWebApplicationFactoryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Host_ShouldNotRunBackgroundOutboxWorker()
    {
        _factory.Services.GetRequiredService<IOptions<OutboxOptions>>().Value.WorkerEnabled
            .Should().BeFalse("the test harness disables the outbox worker and dispatches explicitly");

        _factory.Services.GetServices<IHostedService>()
            .Should().NotContain(service => service is OutboxWorker);
    }
}
