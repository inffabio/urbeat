using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Urbeat.Domain.Entities;

namespace Urbeat.Application.SystemParameters;

/// <summary>
/// Server-side validation for system parameter values. Values are persisted in
/// their canonical invariant form; formatted pt-BR values (for example
/// <c>R$ 19,90</c>) are rejected instead of guessed.
/// </summary>
public static class SystemParameterValidator
{
    public static bool TryNormalize(
        SystemParameterPolicy policy,
        SystemParameterType type,
        string? rawValue,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            error = "Informe um valor.";
            return false;
        }

        if (rawValue.Length > policy.MaxLength)
        {
            error = $"O valor excede o limite de {policy.MaxLength} caracteres.";
            return false;
        }

        if (!TryNormalizeByType(policy, type, rawValue, out normalizedValue, out error))
        {
            return false;
        }

        if (policy.Pattern is { Length: > 0 })
        {
            if (!Regex.IsMatch(normalizedValue, policy.Pattern, RegexOptions.CultureInvariant))
            {
                error = policy.PatternMessage ?? "O valor não corresponde ao formato esperado.";
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeByType(
        SystemParameterPolicy policy,
        SystemParameterType type,
        string rawValue,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = string.Empty;
        error = string.Empty;

        switch (type)
        {
            case SystemParameterType.Boolean:
                return TryNormalizeBoolean(rawValue, out normalizedValue, out error);

            case SystemParameterType.Int32:
                return TryNormalizeInt32(policy, rawValue, out normalizedValue, out error);

            case SystemParameterType.Decimal:
                return TryNormalizeDecimal(policy, rawValue, out normalizedValue, out error);

            case SystemParameterType.String:
            case SystemParameterType.Json:
                return TryNormalizeText(policy, rawValue, out normalizedValue, out error);

            default:
                error = "Tipo de parâmetro não suportado.";
                return false;
        }
    }

    private static bool TryNormalizeBoolean(string rawValue, out string normalizedValue, out string error)
    {
        normalizedValue = string.Empty;
        error = string.Empty;

        switch (rawValue.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
                normalizedValue = "true";
                return true;
            case "false":
            case "0":
                normalizedValue = "false";
                return true;
            default:
                error = "Informe true ou false.";
                return false;
        }
    }

    private static bool TryNormalizeInt32(
        SystemParameterPolicy policy,
        string rawValue,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = string.Empty;
        error = string.Empty;

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = "Informe um número inteiro válido.";
            return false;
        }

        if (!IsWithinRange(policy, parsed))
        {
            error = BuildRangeError(policy);
            return false;
        }

        normalizedValue = parsed.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryNormalizeDecimal(
        SystemParameterPolicy policy,
        string rawValue,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = string.Empty;
        error = string.Empty;

        // Only invariant dot-decimal values are accepted. Formats such as
        // "19,90" or "R$ 19,90" must be corrected by the client, not coerced.
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!decimal.TryParse(rawValue.Trim(), styles, CultureInfo.InvariantCulture, out var parsed))
        {
            error = "Informe um número decimal usando ponto como separador (ex.: 19.90).";
            return false;
        }

        if (!IsWithinRange(policy, parsed))
        {
            error = BuildRangeError(policy);
            return false;
        }

        normalizedValue = parsed.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryNormalizeText(
        SystemParameterPolicy policy,
        string rawValue,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = rawValue;
        error = string.Empty;

        switch (policy.Display)
        {
            case SystemParameterDisplayKind.Url:
                if (!Uri.TryCreate(rawValue, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    error = "Informe uma URL absoluta válida iniciada por http ou https.";
                    return false;
                }
                break;

            case SystemParameterDisplayKind.Email:
                if (!MailAddress.TryCreate(rawValue, out var address)
                    || !string.Equals(address.Address, rawValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    error = "Informe um endereço de e-mail válido.";
                    return false;
                }
                break;
        }

        return true;
    }

    private static bool IsWithinRange(SystemParameterPolicy policy, decimal value)
    {
        if (policy.MinValue is { } min && value < min) return false;
        if (policy.MaxValue is { } max && value > max) return false;
        return true;
    }

    private static string BuildRangeError(SystemParameterPolicy policy)
    {
        if (policy.MinValue is { } min && policy.MaxValue is { } max)
        {
            return $"O valor deve estar entre {FormatBound(min)} e {FormatBound(max)}.";
        }

        if (policy.MinValue is { } onlyMin)
        {
            return $"O valor deve ser maior ou igual a {FormatBound(onlyMin)}.";
        }

        if (policy.MaxValue is { } onlyMax)
        {
            return $"O valor deve ser menor ou igual a {FormatBound(onlyMax)}.";
        }

        return "O valor está fora do intervalo permitido.";
    }

    private static string FormatBound(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
