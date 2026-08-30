using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Urbeat.IntegrationTests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"urbeat-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsaasWebhook:Token"] = "test-asaas-token",
                ["Outbox:WorkerEnabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddSingleton<IEmailTokenCache>(new Mock<IEmailTokenCache>().Object); 
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            services.AddDbContextPool<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            services.RemoveAll<IShortIdCache>();
            services.AddSingleton<IShortIdCache, InMemoryShortIdCache>();

            services.RemoveAll<IImageUploadService>();
            services.AddSingleton<IImageUploadService, FakeImageUploadService>();

            services.RemoveAll(typeof(Microsoft.AspNetCore.SignalR.IHubContext<>));
            services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.IHubContext<>), typeof(RecordingHubContext<>));

            // Program.cs relies on MigrateAsync which fails on InMemory,
            // so the built-in seeders never run. Seed reference data here.
            services.AddHostedService<TestDataSeeder>();
        });
    }

    public async Task DispatchOutboxAsync(CancellationToken cancellationToken = default)
    {
        // Drain until no message is claimable, mirroring the worker's polling loop. This matters for
        // per-order sequencing: a later event only becomes claimable once the earlier event has
        // reached a terminal state, which may take more than one dispatch pass.
        while (true)
        {
            using var scope = Services.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
            if (await dispatcher.DispatchBatchAsync(cancellationToken) == 0)
            {
                break;
            }
        }
    }
}
