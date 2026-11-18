namespace Urbeat.Domain.Entities;

/// <summary>
/// Periodicidade cobrada pela assinatura. O valor é persistido como inteiro e
/// permanece estável no contrato JSON (Monthly = 1, Annual = 2).
/// </summary>
public enum SubscriptionBillingPeriod
{
    Monthly = 1,
    Annual = 2
}
