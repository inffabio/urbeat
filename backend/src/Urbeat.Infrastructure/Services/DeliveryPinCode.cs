using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Urbeat.Domain.Entities;

namespace Urbeat.Infrastructure.Services;

/// <summary>
/// Authoritative delivery PIN hashing/verification shared by the authenticated driver portal and
/// the public route link. Plain PIN values are never persisted and never logged.
/// </summary>
internal static class DeliveryPinCode
{
    private static readonly PasswordHasher<Order> Hasher = new();

    public static string Generate()
    {
        return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
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
