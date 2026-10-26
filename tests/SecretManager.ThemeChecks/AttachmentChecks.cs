using System.Runtime.InteropServices.WindowsRuntime;
using SecretManager.App.Controls;
using SecretManager.App.Services;
using SecretManager.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SecretManager.ThemeChecks;

internal static class AttachmentChecks
{
    public static async Task<int> RunAsync(Expander card, string output, Func<string, Task> snapshot)
    {
        var folderPath = Path.Combine(output, "attachment-fixtures"); Directory.CreateDirectory(folderPath);
        var binaryPath = Path.Combine(folderPath, "key.bin"); byte[] binary = [0, 255, 1, 128, 0];
        await File.WriteAllBytesAsync(binaryPath, binary);
        var binaryFile = await StorageFile.GetFileFromPathAsync(binaryPath);
        using var bitmapStream = await MakeImage(BitmapEncoder.BmpEncoderId);
        using var pngStream = await MakeImage(BitmapEncoder.PngEncoderId);
        var pngPath = Path.Combine(folderPath, "sample.png");
        using (var source = pngStream.AsStreamForRead()) using (var target = File.Create(pngPath)) await source.CopyToAsync(target);
        var pngFile = await StorageFile.GetFileFromPathAsync(pngPath);
        var active = true;
        using var input = new AttachmentInput();
        input.Configure(new() { Theme = AppTheme.Dark }, true, () => active);
        card.Content = input;
        await input.SetImageModeAsync(false);
        await input.SetPathAsync(binaryPath);
        Check(input.IsValidForCurrentMode && Convert.FromBase64String(input.Attachment!.Base64).SequenceEqual(binary), "Desktop picker path did not load exact bytes.");
        var fileDrop = new DataPackage(); fileDrop.SetStorageItems([binaryFile]);
        await input.ReadDataAsync(fileDrop.GetView());
        Check(input.IsValidForCurrentMode && input.Attachment?.FileName == "key.bin", "File drop was not accepted.");
        Check(Convert.FromBase64String(input.Attachment!.Base64).SequenceEqual(binary), "File bytes were changed.");
        await snapshot("file-drop.png");

        await input.SetImageModeAsync(true);
        Check(!input.IsValidForCurrentMode, "Binary file was accepted as an image after switching modes.");
        var pngDrop = new DataPackage(); pngDrop.SetStorageItems([pngFile]);
        await input.ReadDataAsync(pngDrop.GetView());
        Check(input.IsValidForCurrentMode && input.Attachment?.ContentType == "image/png", "PNG drop failed.");
        using var jpegStream = await MakeImage(BitmapEncoder.JpegEncoderId);
        var jpegPath = Path.Combine(folderPath, "sample.jpg");
        using (var source = jpegStream.AsStreamForRead()) using (var target = File.Create(jpegPath)) await source.CopyToAsync(target);
        await input.SetPathAsync(jpegPath);
        Check(input.IsValidForCurrentMode && input.Attachment?.ContentType == "image/jpeg", "JPEG import failed.");
        var previous = input.Attachment;
        var multiple = new DataPackage(); multiple.SetStorageItems([pngFile, binaryFile]);
        await input.ReadDataAsync(multiple.GetView());
        Check(input.Attachment == previous && input.ErrorMessage.Length > 0, "Multiple files replaced a valid image.");
        var directory = new DataPackage(); directory.SetStorageItems([await StorageFolder.GetFolderFromPathAsync(folderPath)]);
        await input.ReadDataAsync(directory.GetView());
        Check(input.Attachment == previous && input.ErrorMessage.Length > 0, "A directory replaced a valid image.");

        var brokenPath = Path.Combine(folderPath, "broken.png");
        await File.WriteAllBytesAsync(brokenPath, [137, 80, 78, 71, 13, 10, 26, 10]);
        await input.SetFileAsync(await StorageFile.GetFileFromPathAsync(brokenPath));
        Check(input.Attachment == previous && input.ErrorMessage.Length > 0, "Corrupt PNG replaced a valid image.");
        var text = new DataPackage(); text.SetText("https://example.invalid/image.png");
        await input.ReadDataAsync(text.GetView());
        Check(input.Attachment == previous && input.ErrorMessage.Length > 0, "A text URL was treated as an image.");
        var oversizedPath = Path.Combine(folderPath, "oversized.bin");
        using (var file = File.Create(oversizedPath)) file.SetLength(VaultValidation.MaxAttachmentBytes + 1);
        await input.SetFileAsync(await StorageFile.GetFileFromPathAsync(oversizedPath));
        Check(input.Attachment == previous && input.ErrorMessage.Length > 0, "An oversized input replaced a valid image.");

        var bitmapData = new DataPackage(); bitmapStream.Seek(0);
        var bitmapReference = RandomAccessStreamReference.CreateFromStream(bitmapStream);
        bitmapData.SetBitmap(bitmapReference);
        await input.ReadDataAsync(bitmapData.GetView());
        Check(input.Attachment is { ContentType: "image/png", FileName: "Изображение из буфера.png" } && input.IsValidForCurrentMode, "Clipboard bitmap was not normalized to PNG.");
        using (var result = await AttachmentImportService.InMemoryAsync(Convert.FromBase64String(input.Attachment!.Base64)))
        {
            var decoded = await BitmapDecoder.CreateAsync(result);
            Check(decoded.PixelWidth == 64 && decoded.PixelHeight == 40, "Clipboard bitmap dimensions changed.");
        }
        await snapshot("image-paste.png");

        var pastedKind = FieldKind.Secret; var pasteError = "";
        using var paste = new AttachmentPasteHandler(input, input, async images =>
        { pastedKind = images ? FieldKind.Image : FieldKind.File; await input.SetImageModeAsync(images); }, () => active, message => pasteError = message);
        Check(!await paste.PasteAsync(text.GetView()), "Text-only clipboard was intercepted instead of native text paste.");
        Check(await paste.PasteAsync(fileDrop.GetView()) && pastedKind == FieldKind.File && input.Attachment?.FileName == "key.bin", "Paste did not automatically select file mode.");
        Check(await paste.PasteAsync(pngDrop.GetView()) && pastedKind == FieldKind.Image && input.Attachment?.ContentType == "image/png", "Paste did not automatically select image mode for PNG.");
        bitmapStream.Seek(0);
        Check(await paste.PasteAsync(bitmapData.GetView()) && pastedKind == FieldKind.Image && input.IsValidForCurrentMode, "Bitmap paste did not work from another field kind.");
        var beforeRejectedPaste = input.Attachment;
        await paste.PasteAsync(multiple.GetView());
        Check(pasteError.Length > 0 && input.Attachment == beforeRejectedPaste, "Multiple-file paste replaced an existing attachment.");

        // Simulate a delayed clipboard provider while the editor/session is being closed.
        var requested = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var delayed = new DataPackage();
        delayed.SetDataProvider(StandardDataFormats.Bitmap, async request =>
        {
            var deferral = request.GetDeferral();
            try { requested.TrySetResult(); await release.Task; request.SetData(bitmapReference); }
            finally { deferral.Complete(); }
        });
        var pending = paste.PasteAsync(delayed.GetView());
        await requested.Task; active = false; paste.Dispose(); input.Dispose(); release.TrySetResult(); await pending;
        Check(input.Attachment is null && !input.IsValidForCurrentMode, "A stale provider restored data after the editor was closed.");
        return 16;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task<InMemoryRandomAccessStream> MakeImage(Guid encoderId)
    {
        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
        var bytes = new byte[64 * 40 * 4];
        for (var y = 0; y < 40; y++) for (var x = 0; x < 64; x++)
        {
            var i = (y * 64 + x) * 4; bytes[i] = 220; bytes[i + 1] = (byte)(80 + y * 3); bytes[i + 2] = (byte)(40 + x * 2); bytes[i + 3] = 255;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 64, 40, 96, 96, bytes);
        await encoder.FlushAsync(); stream.Seek(0); return stream;
    }
}
