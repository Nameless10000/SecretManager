using System.Security.Cryptography;
using SecretManager.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SecretManager.App.Services;

public sealed class AttachmentImportService
{
    public async Task<AttachmentContent> FromPathAsync(string path, bool imagesOnly)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await AttachmentContent.ReadBoundedAsync(input);
        try
        {
            var content = AttachmentContent.FromBytes(Path.GetFileName(path), bytes, imagesOnly);
            if (imagesOnly) await ValidateImageBytesAsync(bytes);
            return content;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public async Task<AttachmentContent> FromFileAsync(StorageFile file, bool imagesOnly)
    {
        using var input = await file.OpenStreamForReadAsync();
        var bytes = await AttachmentContent.ReadBoundedAsync(input);
        try
        {
            var content = AttachmentContent.FromBytes(file.Name, bytes, imagesOnly, file.ContentType);
            if (imagesOnly) await ValidateImageBytesAsync(bytes);
            return content;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task<AttachmentContent> FromDataAsync(DataPackageView data, bool imagesOnly)
    {
        if (imagesOnly && data.Contains(StandardDataFormats.Bitmap))
        {
            var reference = await data.GetBitmapAsync(); using var source = await reference.OpenReadAsync();
            using var input = source.AsStreamForRead();
            var bytes = await AttachmentContent.ReadBoundedAsync(input, VaultValidation.MaxDocumentBytes);
            try
            {
                using var encoded = await InMemoryAsync(bytes);
                var decoder = await BitmapDecoder.CreateAsync(encoded); CheckDimensions(decoder);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                using var png = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
                encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync(); png.Seek(0);
                using var output = png.AsStreamForRead();
                var result = await AttachmentContent.ReadBoundedAsync(output);
                try { return AttachmentContent.FromBytes("Изображение из буфера.png", result, true, "image/png"); }
                finally { CryptographicOperations.ZeroMemory(result); }
            }
            catch (System.Runtime.InteropServices.COMException ex) { throw new InvalidDataException("Не удалось прочитать изображение из буфера.", ex); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var items = await data.GetStorageItemsAsync();
            if (items.Count != 1 || items[0] is not StorageFile file)
                throw new InvalidDataException("Перетащите один файл. Папки и несколько файлов в одном поле не поддерживаются.");
            return await FromFileAsync(file, imagesOnly);
        }
        throw new InvalidDataException(imagesOnly ? "Нет изображения. Скопируйте картинку или PNG/JPEG-файл, а не текст или ссылку." : "Перетащите файл из Проводника.");
    }

    public async Task<AttachmentContent> ValidateExistingAsync(AttachmentContent content, bool imagesOnly)
    {
        var bytes = Convert.FromBase64String(content.Base64);
        try
        {
            var result = AttachmentContent.FromBytes(content.FileName, bytes, imagesOnly, content.ContentType);
            if (imagesOnly) await ValidateImageBytesAsync(bytes);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static async Task ValidateImageBytesAsync(byte[] bytes)
    {
        try
        {
            using var input = await InMemoryAsync(bytes);
            var decoder = await BitmapDecoder.CreateAsync(input); CheckDimensions(decoder);
            using var decoded = await decoder.GetSoftwareBitmapAsync(); // Detect corrupt pixel data, not just a matching signature.
        }
        catch (System.Runtime.InteropServices.COMException ex) { throw new InvalidDataException("Изображение повреждено или его формат не поддерживается.", ex); }
    }
    private static void CheckDimensions(BitmapDecoder decoder)
    {
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || (long)decoder.PixelWidth * decoder.PixelHeight > 40_000_000)
            throw new InvalidDataException("Изображение должно быть не больше 40 мегапикселей.");
    }
    public static async Task<InMemoryRandomAccessStream> InMemoryAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using var writer = new DataWriter(stream.GetOutputStreamAt(0));
            writer.WriteBytes(bytes); await writer.StoreAsync(); stream.Seek(0); return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
