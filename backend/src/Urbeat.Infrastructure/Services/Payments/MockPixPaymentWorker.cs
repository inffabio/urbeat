using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.Payments;

/// <summary>
/// Background worker that periodically applies due mock Pix payment transitions. It is registered
/// only when <c>Payments:Provider=Mock</c> and the worker is enabled; the real gateway path never
/// runs it.
/// </summary>
public sealed class MockPixPaymentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MockPixOptions _options;
    private readonly ILogger<MockPixPaymentWorker> _logger;

    public MockPixPaymentWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MockPixOptions> options,
        ILogger<MockPixPaymentWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsMockEnabled)
        {
            return;
        }

        _logger.LogInformation("Mock Pix payment worker started.");

        var pollingInterval = TimeSpan.FromSeconds(_options.WorkerPollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<MockPixPaymentProcessor>();
                var processed = await processor.ProcessDuePaymentsAsync(stoppingToken);
                if (processed > 0)
                {
                    _logger.LogInformation("Mock Pix payment worker processed {Count} payment(s).", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Mock Pix payment worker iteration failed.");
            }

            try
            {
                await Task.Delay(pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Mock Pix payment worker stopped.");
    }
}
