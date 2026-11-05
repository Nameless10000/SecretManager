using System.Security.Cryptography;

namespace SecretManager.Core.Services;

public static class PasswordGenerator
{
    public static string Generate(int length = 24, bool symbols = true, bool excludeSimilar = true)
    {
        if (length is < 12 or > 128)
        {
            throw new ArgumentException("Длина пароля: 12–128 символов.");
        }

        var groups = new[] { "abcdefghjkmnpqrstuvwxyz", "ABCDEFGHJKMNPQRSTUVWXYZ", "23456789" }.ToList();
        if (!excludeSimilar)
        {
            groups[0] += "ilo";
            groups[1] += "ILO";
            groups[2] += "01";
        }

        if (symbols)
        {
            groups.Add("!@#$%&*+-=?_");
        }

        var alphabet = string.Concat(groups);
        var result = new char[length];
        for (var index = 0; index < length; index++)
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
}
