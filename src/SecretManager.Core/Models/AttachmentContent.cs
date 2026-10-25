using System.Buffers;
using System.Security.Cryptography;

namespace SecretManager.Core.Models;

public sealed record AttachmentContent(string FileName, string ContentType, string Base64, int Length)
{
    public static AttachmentContent FromBytes(string fileName, byte[] bytes, bool imagesOnly, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || fileName.Contains('/') || fileName.Contains('\\') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Некорректное имя вложения.");
        if (bytes.Length > VaultValidation.MaxAttachmentBytes) throw new InvalidDataException("Вложение должно быть не больше 10 МБ.");
        var image = VaultValidation.IsSupportedImage(bytes);
        if (imagesOnly && !image) throw new InvalidDataException("Для изображения выберите PNG или JPEG.");
        if (image) contentType = bytes[0] == 137 ? "image/png" : "image/jpeg";
        return new(fileName, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Convert.ToBase64String(bytes), bytes.Length);
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream source, int maxBytes = VaultValidation.MaxAttachmentBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes is <= 0 or > VaultValidation.MaxDocumentBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(81920, maxBytes + 1));
        using var output = new MemoryStream();
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes + 1L - output.Length)), cancellationToken);
                if (read == 0) return output.ToArray();
                if (output.Length + read > maxBytes) throw new InvalidDataException("Размер вложения превышает допустимый лимит.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer); ArrayPool<byte>.Shared.Return(buffer);
            if (output.TryGetBuffer(out var written)) CryptographicOperations.ZeroMemory(written.AsSpan());
        }
    }
}
