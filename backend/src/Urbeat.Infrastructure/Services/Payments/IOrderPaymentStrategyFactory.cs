using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services.Payments;

public interface IOrderPaymentStrategyFactory
{
    IOrderPaymentStrategy? Resolve(PaymentMethod method);
}
