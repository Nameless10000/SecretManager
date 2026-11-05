using System.Security.Cryptography;
using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public static class PasswordGenerator
{
    public static string Generate(int length = 24, bool symbols = true, bool excludeSimilar = true)
    {
        return Generate(new PasswordGeneratorOptions { Length = length, Symbols = symbols, ExcludeSimilar = excludeSimilar });
    }

    public static string Generate(PasswordGeneratorOptions options)
    {
        ValidateOptions(options);
        var groups = new List<string>();
        if (options.Lowercase)
        {
            groups.Add(options.ExcludeSimilar ? "abcdefghjkmnpqrstuvwxyz" : "abcdefghijklmnopqrstuvwxyz");
        }

        if (options.Uppercase)
        {
            groups.Add(options.ExcludeSimilar ? "ABCDEFGHJKMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        }

        if (options.Digits)
        {
            groups.Add(options.ExcludeSimilar ? "23456789" : "0123456789");
        }

        if (options.Symbols)
        {
            groups.Add("!@#$%&*+-=?_");
        }

        var alphabet = string.Concat(groups);
        var result = new char[options.Length];
        for (var index = 0; index < result.Length; index++)
        {
            var source = index < groups.Count ? groups[index] : alphabet;
            result[index] = source[RandomNumberGenerator.GetInt32(source.Length)];
        }

        for (var index = result.Length - 1; index > 0; index--)
        {
            var other = RandomNumberGenerator.GetInt32(index + 1);
            (result[index], result[other]) = (result[other], result[index]);
        }

        return new string(result);
    }

    public static void ValidateOptions(PasswordGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Length is < 4 or > 128)
        {
            throw new ArgumentException("Длина пароля: 4–128 символов.");
        }

        if (!options.Lowercase && !options.Uppercase && !options.Digits && !options.Symbols)
        {
            throw new ArgumentException("Выберите хотя бы один набор символов.");
        }
    }
}
