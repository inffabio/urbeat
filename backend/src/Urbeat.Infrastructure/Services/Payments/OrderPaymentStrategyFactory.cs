using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services.Payments;

public sealed class OrderPaymentStrategyFactory : IOrderPaymentStrategyFactory
{
    private readonly IReadOnlyCollection<IOrderPaymentStrategy> _strategies;

    public OrderPaymentStrategyFactory(IEnumerable<IOrderPaymentStrategy> strategies)
    {
        _strategies = strategies.ToArray();
    }

    public IOrderPaymentStrategy? Resolve(PaymentMethod method)
    {
        return _strategies.FirstOrDefault(x => x.CanHandle(method));
    }
}
