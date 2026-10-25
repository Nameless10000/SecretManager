using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecretManager.Core.Models;

namespace SecretManager.Core.Security;

public sealed record EncryptedEnvelope(int Version, int Iterations, string Salt, string Nonce, string Tag, string Ciphertext);

public static class VaultCrypto
{
    public const int Iterations = 600_000;
    public const int MaxEnvelopeBytes = 90 * 1024 * 1024;
    public const string LocalContext = "SecretManager/local/v1";
    public const string ExportContext = "SecretManager/export/v1";

    public static void ValidateNewPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 1024)
            throw new ArgumentException("Используйте пароль длиной от 12 до 1024 символов.");
    }

    public static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

    public static (EncryptedEnvelope Envelope, byte[] Key) Protect(VaultDocument document, string password, string context)
    {
        ValidateNewPassword(password);
        var salt = RandomNumberGenerator.GetBytes(32);
        var key = DeriveKey(password, salt);
        try { return (Encrypt(document, key, salt, context), key); }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    public static EncryptedEnvelope Encrypt(VaultDocument document, byte[] key, byte[] salt, string context)
    {
        VaultValidation.Validate(document);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, VaultValidation.JsonOptions);
        try
        {
            if (plaintext.Length > VaultValidation.MaxDocumentBytes)
                throw new InvalidDataException("Хранилище превышает 64 МБ. Уменьшите размер вложений.");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(context));
            return new(1, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static (VaultDocument Document, byte[] Key) Unprotect(EncryptedEnvelope envelope, string password, string context)
    {
        ValidateEnvelope(envelope);
        if (password.Length > 1024) throw new CryptographicException("Неверный пароль или повреждённый файл.");
        var key = DeriveKey(password, Convert.FromBase64String(envelope.Salt));
        try
        {
            var cipher = Convert.FromBase64String(envelope.Ciphertext);
            var plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(Convert.FromBase64String(envelope.Nonce), cipher, Convert.FromBase64String(envelope.Tag),
                    plain, Encoding.UTF8.GetBytes(context));
                var document = JsonSerializer.Deserialize<VaultDocument>(plain, VaultValidation.JsonOptions)
                    ?? throw new InvalidDataException("Пустое хранилище.");
                VaultValidation.Validate(document);
                return (document, key);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    public static EncryptedEnvelope Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxEnvelopeBytes)
            throw new InvalidDataException("Файл слишком велик.");
        try
        {
            var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>(json)
                ?? throw new InvalidDataException("Пустой файл.");
            ValidateEnvelope(envelope);
            return envelope;
        }
        catch (JsonException) { throw new InvalidDataException("Неверный формат зашифрованного файла."); }
    }

    private static void ValidateEnvelope(EncryptedEnvelope e)
    {
        if (e.Version != 1 || e.Iterations != Iterations || e.Salt is null || e.Nonce is null ||
            e.Tag is null || e.Ciphertext is null || e.Ciphertext.Length > MaxEnvelopeBytes)
            throw new InvalidDataException("Неподдерживаемый формат шифрования.");
        try
        {
            if (Convert.FromBase64String(e.Salt).Length != 32 || Convert.FromBase64String(e.Nonce).Length != 12 ||
                Convert.FromBase64String(e.Tag).Length != 16 || e.Ciphertext.Length > (VaultValidation.MaxDocumentBytes + 2L) / 3 * 4)
                throw new InvalidDataException("Неверные параметры шифрования.");
            // Validate before deriving a key, including malformed ciphertext.
            _ = Convert.FromBase64String(e.Ciphertext);
        }
        catch (FormatException) { throw new InvalidDataException("Неверная кодировка файла."); }
    }
}
