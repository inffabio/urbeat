using System;
using System.Linq;

namespace Urbeat.Application.Validators;

public static class DocumentValidator
{
    public static bool IsCpfOrCnpjValid(string? document)
    {
        if (string.IsNullOrWhiteSpace(document)) return false;

        var cleanDoc = new string(document.Where(char.IsDigit).ToArray());

        if (cleanDoc.Length == 11)
            return IsCpfValid(cleanDoc);
        if (cleanDoc.Length == 14)
            return IsCnpjValid(cleanDoc);

        return false;
    }

    private static bool IsCpfValid(string cpf)
    {
        if (cpf.All(c => c == cpf[0])) return false;

        var multiplicador1 = new int[9] { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multiplicador2 = new int[10] { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

        var tempCpf = cpf.Substring(0, 9);
        var soma = 0;

        for (int i = 0; i < 9; i++)
            soma += int.Parse(tempCpf[i].ToString()) * multiplicador1[i];

        var resto = soma % 11;
        resto = resto < 2 ? 0 : 11 - resto;

        var digito = resto.ToString();
        tempCpf = tempCpf + digito;
        soma = 0;

        for (int i = 0; i < 10; i++)
            soma += int.Parse(tempCpf[i].ToString()) * multiplicador2[i];

        resto = soma % 11;
        resto = resto < 2 ? 0 : 11 - resto;

        digito = digito + resto.ToString();

        return cpf.EndsWith(digito);
    }

    private static bool IsCnpjValid(string cnpj)
    {
        if (cnpj.All(c => c == cnpj[0])) return false;

        var multiplicador1 = new int[12] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multiplicador2 = new int[13] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var tempCnpj = cnpj.Substring(0, 12);
        var soma = 0;

        for (int i = 0; i < 12; i++)
            soma += int.Parse(tempCnpj[i].ToString()) * multiplicador1[i];

        var resto = (soma % 11);
        resto = resto < 2 ? 0 : 11 - resto;

        var digito = resto.ToString();
        tempCnpj = tempCnpj + digito;
        soma = 0;

        for (int i = 0; i < 13; i++)
            soma += int.Parse(tempCnpj[i].ToString()) * multiplicador2[i];

        resto = (soma % 11);
        resto = resto < 2 ? 0 : 11 - resto;

        digito = digito + resto.ToString();

        return cnpj.EndsWith(digito);
    }
}