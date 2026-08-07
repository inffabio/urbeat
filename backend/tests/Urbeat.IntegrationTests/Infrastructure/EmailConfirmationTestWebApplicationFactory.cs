using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Urbeat.Application.Interfaces;
using Urbeat.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Urbeat.IntegrationTests.Infrastructure;

/// <summary>
/// Web factory dedicated to RF77 (email confirmation) tests.
/// - Replaces the EF Core provider with an in-memory database.
/// - Replaces <see cref="IEmailService"/> with <see cref="FakeEmailService"/> so we can inspect what was sent.
/// - Replaces <see cref="IBackgroundJobClient"/> with a synchronous one so that the email confirmation
///   job runs in the same request, avoiding the asynchronous Hangfire pipeline.
/// </summary>
public sealed class EmailConfirmationTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"urbeat-email-tests-{Guid.NewGuid()}";

    public FakeEmailService EmailService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AsaasWebhook:Token"] = "test-asaas-token",
                ["EmailConfirmation:FrontendBaseUrl"] = "https://app.urbeat.test",
                ["EmailConfirmation:ConfirmPath"] = "/confirm-email"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddSingleton<IEmailTokenCache>(new FakeEmailTokenCache()); 
            services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            services.AddDbContextPool<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(EmailService);

            services.RemoveAll<IBackgroundJobClient>();
            services.AddSingleton<IBackgroundJobClient>(provider =>
                new SynchronousBackgroundJobClient(provider));

            services.AddHostedService<TestDataSeeder>();
        });
    }

    private sealed class SynchronousBackgroundJobClient : IBackgroundJobClient
    {
        private readonly IServiceProvider _provider;

        public SynchronousBackgroundJobClient(IServiceProvider provider)
        {
            _provider = provider;
        }

        public string? Create(Job job, IState state)
        {
            using var scope = _provider.CreateScope();
            var instance = job.Type.IsAbstract || job.Type.IsInterface
                ? scope.ServiceProvider.GetRequiredService(job.Type)
                : ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, job.Type);

            var arguments = job.Args?.ToArray() ?? Array.Empty<object?>();
            var result = job.Method.Invoke(instance, arguments);
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }

            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState)
        {
            return true;
        }
    }
}
