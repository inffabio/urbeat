namespace Urbeat.Domain.Services;

/// <summary>
/// Authoritative rules for the Brazilian mobile numbers stored by the motoboy registration.
/// The seller may type the value with or without the visual mask; persistence keeps only the
/// digits. A valid mobile number has a 2-digit DDD and 9 subscriber digits starting with 9.
/// </summary>
public static class BrazilianMobilePhone
{
    public const int DigitCount = 11;

    /// <summary>Only digits are persisted; every visual separator is discarded.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> digits = stackalloc char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
            {
                digits[length++] = character;
            }
        }

        return new string(digits[..length]);
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !HasOnlyAcceptedCharacters(value))
        {
            return false;
        }

        var digits = Normalize(value);
        if (digits.Length != DigitCount)
        {
            return false;
        }

        var ddd = int.Parse(digits.AsSpan(0, 2));
        return ddd is >= 11 and <= 99 && digits[2] == '9';
    }

    private static bool HasOnlyAcceptedCharacters(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character)
                && character is not (' ' or '(' or ')' or '-' or '+'))
            {
                return false;
            }
        }

        return true;
    }
}
