using Microsoft.AspNetCore.Identity;
using Urbeat.Domain.Entities;
using Urbeat.Domain.Services;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Authoritative delivery PIN hashing/verification shared by the authenticated driver portal and
/// the public route link. The PIN is deterministic: the last four digits of the customer's phone
/// number. Plain PIN values are never persisted and never logged.
/// </summary>
internal static class DeliveryPinCode
{
    /// <summary>Authoritative PIN length. Every new delivery PIN has exactly this many digits.</summary>
    public const int Length = 4;

    private static readonly PasswordHasher<Order> Hasher = new();

    /// <summary>
    /// Derives the PIN from the last four digits of the customer's phone. There is deliberately no
    /// random fallback: an order without a phone that has at least four digits cannot enter
    /// delivery, because the customer would have no deterministic PIN to share with the motoboy.
    /// </summary>
    public static bool TryFromCustomerPhone(string? phoneNumber, out string pin)
    {
        var digits = BrazilianMobilePhone.Normalize(phoneNumber);
        if (digits.Length < Length)
        {
            pin = string.Empty;
            return false;
        }

        pin = digits[^Length..];
        return true;
    }

    public static string Hash(Order order, string pin)
    {
        return Hasher.HashPassword(order, pin);
    }

    public static bool Verify(Order order, string hash, string pin)
    {
        var result = Hasher.VerifyHashedPassword(order, hash, pin);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
