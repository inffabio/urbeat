using Urbeat.Domain.Entities;

namespace Urbeat.Domain.Services;

/// <summary>
/// Authoritative payment policy for the storefront checkout and for the motoboy handover.
///
/// The storefront no longer offers online Pix for new orders. New orders must choose one of the
/// four pay-on-delivery options (Pix on the card machine, cash, debit or credit). Legacy online
/// values remain readable so old orders and in-flight Pix transactions are never broken, but they
/// are rejected when a new order is created.
///
/// Cash orders must explicitly declare whether the customer needs change. When change is needed,
/// the declared amount must be at least the authoritative order total and use currency precision
/// (two decimal places). The motoboy later informs the amount actually received; the server
/// recomputes the change and never trusts a client-computed value.
/// </summary>
public static class OrderPaymentPolicy
{
    /// <summary>Upper bound accepted for any cash amount, protecting the API from abuse.</summary>
    public const decimal MaxCashAmount = 100_000m;

    /// <summary>Methods a new checkout may select.</summary>
    public static bool SupportsNewOrder(PaymentMethod method) =>
        method is PaymentMethod.CashOnDelivery
            or PaymentMethod.PixOnDelivery
            or PaymentMethod.DebitOnDelivery
            or PaymentMethod.CreditOnDelivery;

    public static bool IsCashOnDelivery(PaymentMethod method) =>
        method == PaymentMethod.CashOnDelivery;

    /// <summary>
    /// True when a cash checkout predates the current cash-change contract: neither the accepted
    /// total nor the change selection is present in the payload. Such checkouts keep working
    /// instead of being rejected, because existing cached storefronts (PWA service worker and
    /// Capacitor webview) cannot send the new fields before they update.
    ///
    /// No cash decision is invented for these orders: <see cref="Order.CashWithoutChange"/> stays
    /// null and <see cref="RequiresHandoverConfirmation"/> stays false, so they keep the previous
    /// PIN-only flow and are never forced through the new handover step.
    /// </summary>
    public static bool IsLegacyCashCheckout(decimal? expectedTotal, bool? cashWithoutChange) =>
        !expectedTotal.HasValue && !cashWithoutChange.HasValue;

    /// <summary>
    /// True when the order was created under the current pay-on-delivery contract and therefore
    /// requires the assigned motoboy to manually confirm the handover/payment before the PIN.
    /// Legacy orders (online, generic card on delivery, or cash without the cash-change choice)
    /// keep the previous flow so in-progress deliveries are never stranded.
    /// </summary>
    public static bool RequiresHandoverConfirmation(Order order) =>
        order.PaymentMethod is PaymentMethod.PixOnDelivery
            or PaymentMethod.DebitOnDelivery
            or PaymentMethod.CreditOnDelivery
        || (order.PaymentMethod == PaymentMethod.CashOnDelivery && order.CashWithoutChange.HasValue);

    public static bool HasCurrencyPrecision(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero) == value;

    /// <summary>
    /// Validates the cash change selection captured at checkout against the authoritative total.
    /// Returns null when valid, or a user-safe error message when invalid.
    /// </summary>
    public static string? ValidateCashSelection(decimal total, bool? withoutChange, decimal? changeFor)
    {
        if (!withoutChange.HasValue)
        {
            return "Informe se o cliente precisa de troco em dinheiro.";
        }

        if (!HasCurrencyPrecision(total))
        {
            return "Total do pedido com precisão monetária inválida.";
        }

        if (withoutChange.Value)
        {
            return changeFor.HasValue
                ? "Pedido sem troco não pode informar valor para troco."
                : null;
        }

        if (!changeFor.HasValue)
        {
            return "Informe o valor para troco quando o cliente precisa de troco.";
        }

        if (!HasCurrencyPrecision(changeFor.Value))
        {
            return "O valor para troco deve ter no máximo duas casas decimais.";
        }

        if (changeFor.Value <= 0m)
        {
            return "O valor para troco deve ser maior que zero.";
        }

        if (changeFor.Value > MaxCashAmount)
        {
            return "O valor para troco excede o limite permitido.";
        }

        return changeFor.Value < total
            ? "O valor para troco deve ser maior ou igual ao total do pedido."
            : null;
    }

    /// <summary>
    /// Validates the cash amount informed by the motoboy at handover and computes the change from
    /// the authoritative order total. The client never supplies the change value.
    /// </summary>
    public static string? ValidateCashReceived(Order order, decimal? received, out decimal change)
    {
        change = 0m;

        if (received is null)
        {
            return "Informe o valor em dinheiro efetivamente recebido.";
        }

        if (!HasCurrencyPrecision(received.Value))
        {
            return "O valor recebido deve ter no máximo duas casas decimais.";
        }

        if (received.Value > MaxCashAmount)
        {
            return "O valor recebido excede o limite permitido.";
        }

        if (received.Value < order.Total)
        {
            return "O valor recebido é menor que o total do pedido.";
        }

        // The change-for amount captured at checkout is a customer forecast, never a cap. The
        // motoboy may receive any valid amount above the authoritative total (for example a larger
        // bill) and the server computes the actual change. This also covers "sem troco" choices:
        // the customer may still hand over a larger amount at the door.
        change = received.Value - order.Total;
        return null;
    }
}
