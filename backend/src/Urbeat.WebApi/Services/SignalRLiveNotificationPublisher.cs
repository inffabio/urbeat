using Microsoft.AspNetCore.SignalR;
using Urbeat.Application.Interfaces;
using Urbeat.WebApi.Hubs;

namespace Urbeat.WebApi.Services;

/// <summary>
/// SignalR implementation of <see cref="ILiveNotificationPublisher"/>.
///
/// The hub contexts are injected as typed <see cref="IHubContext{THub}"/> instances resolved by the
/// composition root. The previous Infrastructure implementation resolved the hub context through
/// reflection and invoked it with <c>dynamic</c>; the concrete SignalR hub context type is internal
/// to the SignalR assembly, so the runtime binder rejected every push. Sending through the public
/// <see cref="IHubContext{THub}"/> interface keeps the call strongly typed.
///
/// Delivery is at-least-once: a transient failure returns <c>false</c> so the outbox retries.
/// Failures are logged with the channel, the event name, the recipient identifier and the exception
/// type only — never the payload, the exception message or its stack trace, which may carry personal
/// data or connection strings.
/// </summary>
public sealed class SignalRLiveNotificationPublisher : ILiveNotificationPublisher
{
    private const string SellerChannel = "seller";
    private const string CustomerChannel = "customer";

    private readonly IHubContext<SellerNotificationHub> _sellerHub;
    private readonly IHubContext<CustomerNotificationHub> _customerHub;
    private readonly ILogger<SignalRLiveNotificationPublisher> _logger;

    public SignalRLiveNotificationPublisher(
        IHubContext<SellerNotificationHub> sellerHub,
        IHubContext<CustomerNotificationHub> customerHub,
        ILogger<SignalRLiveNotificationPublisher> logger)
    {
        _sellerHub = sellerHub;
        _customerHub = customerHub;
        _logger = logger;
    }

    public Task<bool> PublishSellerAsync(
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(_sellerHub.Clients, SellerChannel, userId, method, payload, cancellationToken);
    }

    public Task<bool> PublishCustomerAsync(
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(_customerHub.Clients, CustomerChannel, userId, method, payload, cancellationToken);
    }

    private async Task<bool> SendAsync(
        IHubClients clients,
        string channel,
        Guid userId,
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            _logger.LogWarning(
                "SignalR live push skipped: missing recipient | Channel={Channel} | Method={Method}",
                channel,
                method);
            return false;
        }

        try
        {
            await clients.User(userId.ToString()).SendCoreAsync(method, new[] { payload }, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The exception object (message and stack trace) is deliberately not logged: transport
            // exceptions from Redis/SignalR may embed connection strings, endpoints or personal data
            // in their text. Only the exception type is safe diagnostic data; the Outbox records the
            // sanitized retry reason separately.
            _logger.LogWarning(
                "SignalR live push failed | Channel={Channel} | Method={Method} | RecipientUserId={RecipientUserId} | ExceptionType={ExceptionType}",
                channel,
                method,
                userId,
                exception.GetType().Name);
            return false;
        }
    }
}
