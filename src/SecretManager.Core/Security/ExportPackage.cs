using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecretManager.Core.Security;

public sealed record ExportPackage(string Format, int Version, string Payload, string Sha256)
{
    public const int MaxPackageBytes = VaultCrypto.MaxEnvelopeBytes + 1024 * 1024;
    public static async Task<string> ReadTextAsync(Stream source, int maxBytes = MaxPackageBytes, CancellationToken cancellationToken = default)
    {
        if (maxBytes is <= 0 or > MaxPackageBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        using var data = new MemoryStream();
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Min(81920, maxBytes + 1));
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes + 1L - data.Length)), cancellationToken);
                if (count == 0) break;
                if (data.Length + count > maxBytes) throw new InvalidDataException("Файл экспорта слишком велик.");
                await data.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            data.Position = 0;
            using var reader = new StreamReader(data, Encoding.UTF8, true, 8192, true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer); System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            if (data.TryGetBuffer(out var bytes)) CryptographicOperations.ZeroMemory(bytes.AsSpan());
        }
    }
    public static string Create(EncryptedEnvelope envelope)
    {
        var payload = JsonSerializer.Serialize(envelope);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return JsonSerializer.Serialize(new ExportPackage("SecretManager.Export", 1, payload, digest));
    }
    public static EncryptedEnvelope Read(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxPackageBytes)
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
