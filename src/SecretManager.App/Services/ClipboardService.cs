using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using SecretManager.Core.Models;

namespace SecretManager.App.Services;

public sealed class ClipboardService : IDisposable
{
    private const string OwnerFormat = "SecretManager.ClipboardOwner";
    private readonly DispatcherTimer timer = new();
    private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretManager", "Clipboard");
    private string? token;
    private DateTimeOffset expires;
    private InMemoryRandomAccessStream? bitmapStream;
    private readonly List<string> tempFiles = [];
    private readonly System.Threading.SemaphoreSlim access = new(1, 1);
    public ClipboardService()
    {
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += async (_, _) => { if (DateTimeOffset.UtcNow >= expires) await ClearOwnedAsync(); };
        // Stale files from an interrupted previous process have no active owner.
        if (Directory.Exists(folder)) foreach (var file in Directory.EnumerateFiles(folder)) TryDelete(file);
    }
    public async Task CopyAsync(SecretField field, int clearSeconds, Func<bool> isCurrent)
    {
        await access.WaitAsync();
        try { await CopyCoreAsync(field, clearSeconds, isCurrent); }
        finally { access.Release(); }
    }
    private async Task CopyCoreAsync(SecretField field, int clearSeconds, Func<bool> isCurrent)
    {
        await ClearCoreAsync();
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        token = Guid.NewGuid().ToString(); package.SetData(OwnerFormat, token);
        if (field.Kind is FieldKind.Text or FieldKind.Secret) package.SetText(field.Value);
        else
        {
            var bytes = Convert.FromBase64String(field.Value);
            if (field.Kind == FieldKind.Image)
            {
                bitmapStream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(bitmapStream.GetOutputStreamAt(0));
                writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync();
                bitmapStream.Seek(0); package.SetBitmap(RandomAccessStreamReference.CreateFromStream(bitmapStream));
            }
            else
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + field.FileName);
                await File.WriteAllBytesAsync(path, bytes);
                tempFiles.Add(path);
                package.SetStorageItems([await StorageFile.GetFileFromPathAsync(path)]);
            }
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
        // Prevent cloud/clipboard history persistence where Windows supports these options.
        var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
        if (!isCurrent()) { await ClearCoreAsync(); throw new OperationCanceledException(); }
        if (!Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(package, options))
            throw new InvalidOperationException("Буфер обмена занят. Попробуйте ещё раз.");
        if (clearSeconds > 0) { expires = DateTimeOffset.UtcNow.AddSeconds(clearSeconds); timer.Start(); }
    }
    public async Task ClearOwnedAsync()
    {
        await access.WaitAsync();
        try { await ClearCoreAsync(); }
        finally { access.Release(); }
    }
    private async Task ClearCoreAsync()
    {
        timer.Stop();
        try
        {
            if (token is not null)
            {
                var current = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (current.Contains(OwnerFormat) && await current.GetDataAsync(OwnerFormat) is string owner && owner == token)
                    Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        { /* Another application or the Windows lock screen may hold the clipboard. */ }
        finally
        {
            token = null; bitmapStream?.Dispose(); bitmapStream = null;
            foreach (var file in tempFiles) TryDelete(file);
            tempFiles.Clear();
        }
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void Dispose() { timer.Stop(); bitmapStream?.Dispose(); foreach (var file in tempFiles) TryDelete(file); }
}
