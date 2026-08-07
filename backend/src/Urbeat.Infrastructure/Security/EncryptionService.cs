using System.Security.Cryptography;
using Urbeat.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Security;

public sealed class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    private static readonly byte[] Nonce = "UrbeatSalt2026!"u8.ToArray();

    public EncryptionService(IOptions<EncryptionOptions> options)
    {
        var keyBase64 = options.Value.Key;
        if (string.IsNullOrWhiteSpace(keyBase64) || keyBase64.Length < 32)
        {
            throw new InvalidOperationException("Encryption key must be at least 32 characters long.");
        }

        _key = System.Text.Encoding.UTF8.GetBytes(keyBase64.PadRight(32).Substring(0, 32));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = Nonce.Take(16).ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Convert.ToBase64String(cipherBytes);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = Nonce.Take(16).ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    public string Key { get; init; } = string.Empty;
}
