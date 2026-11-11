using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services;
using Urbeat.Infrastructure.Services.WhatsApp;
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
                ["WhatsApp:WebhookEnabled"] = "true",
                ["WhatsApp:WebhookVerifyToken"] = "test-whatsapp-verify-token",
                ["WhatsApp:AppSecret"] = "test-whatsapp-app-secret",
                ["WhatsApp:AccessToken"] = "test-whatsapp-access-token",
                ["WhatsApp:PhoneNumberId"] = "1436364406224525",
                ["WhatsApp:GraphApiVersion"] = WhatsAppOptions.DefaultGraphApiVersion,
                ["Outbox:WorkerEnabled"] = "false",
                // Dedicated, explicit test key: production has no default and fails closed without
                // a valid RolePromotionChallenge:SigningKey.
                ["RolePromotionChallenge:SigningKey"] = "urbeat-integration-tests-role-promotion-signing-key-2026",
                // Registration/promotion tests exercise many accounts from the same test client.
                ["RateLimiting:RolePromotionPermitLimit"] = "10000",
                ["RateLimiting:RolePromotionWindowSeconds"] = "1",
                // Explicitly weaker Argon2id parameters for the test process only. Production keeps
                // the strong defaults from appsettings.json (64 MiB, t=3, p=2).
                ["PasswordHashing:Argon2id:MemorySizeKb"] = "8192",
                ["PasswordHashing:Argon2id:Iterations"] = "2",
                ["PasswordHashing:Argon2id:DegreeOfParallelism"] = "1"
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

            // Tests own outbox dispatch; a background poller would mutate the shared fixture
            // database asynchronously while assertions run.
            services.RemoveBackgroundOutboxWorker();
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

            var dispatched = 0;
            foreach (var channel in Enum.GetValues<OutboxChannel>())
            {
                dispatched += await dispatcher.DispatchBatchAsync(channel, cancellationToken);
            }

            if (dispatched == 0)
            {
                break;
            }
        }
    }
}
