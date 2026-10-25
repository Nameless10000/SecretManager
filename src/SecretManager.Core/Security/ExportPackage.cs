using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecretManager.Core.Security;

public sealed record ExportPackage(string Format, int Version, string Payload, string Sha256)
{
    public static string Create(EncryptedEnvelope envelope)
    {
        var payload = JsonSerializer.Serialize(envelope);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return JsonSerializer.Serialize(new ExportPackage("SecretManager.Export", 1, payload, digest));
    }
    public static EncryptedEnvelope Read(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > VaultCrypto.MaxEnvelopeBytes + 1024 * 1024)
            throw new InvalidDataException("Файл экспорта слишком велик.");
        ExportPackage package;
        try { package = JsonSerializer.Deserialize<ExportPackage>(json) ?? throw new InvalidDataException("Пустой экспорт."); }
        catch (JsonException) { throw new InvalidDataException("Неверный формат экспорта."); }
        if (package.Format != "SecretManager.Export" || package.Version != 1 || package.Payload is null || package.Sha256?.Length != 64)
            throw new InvalidDataException("Неподдерживаемый формат экспорта.");
        byte[] expected;
        try { expected = Convert.FromHexString(package.Sha256); }
        catch (FormatException) { throw new InvalidDataException("Неверная контрольная сумма."); }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(package.Payload));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidDataException("Контрольная сумма SHA-256 не совпала. Файл повреждён или изменён.");
        // The digest detects transport damage. AES-GCM authentication is still mandatory,
        // even if an attacker recomputes this public checksum after changing the payload.
        return VaultCrypto.Parse(package.Payload);
    }
}
