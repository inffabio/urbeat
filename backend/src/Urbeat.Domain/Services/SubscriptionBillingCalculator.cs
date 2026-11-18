namespace Urbeat.Domain.Services;

using Urbeat.Domain.Entities;

/// <summary>
/// Cálculos de cobrança da assinatura. Todas as datas retornadas são UTC e o
/// servidor é a única autoridade sobre trial, vencimento e janela cobrada.
/// </summary>
public static class SubscriptionBillingCalculator
{
    public static DateTime CalculateFirstChargeDueUtc(int trialDays, DateTime subscriptionStartUtc, DateTime nowUtc)
    {
        if (trialDays <= 0)
        {
            return nowUtc;
        }

        var trialEndUtc = subscriptionStartUtc.AddDays(trialDays);
        return trialEndUtc > nowUtc ? trialEndUtc : nowUtc;
    }

    public static DateTime CalculatePeriodEndUtc(DateTime startUtc, SubscriptionBillingPeriod period)
        => period switch
        {
            SubscriptionBillingPeriod.Annual => startUtc.AddYears(1),
            _ => startUtc.AddMonths(1)
        };
}
