using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using SecretManager.App.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace SecretManager.App.Controls;

internal sealed class AttachmentPasteHandler : IDisposable
{
    private readonly UIElement root;
    private readonly AttachmentInput input;
    private readonly Func<bool, Task> changeMode;
    private readonly Func<bool> canUse;
    private readonly Action<string> reportError;
    private bool disposed;
    private bool busy;
    internal AttachmentPasteHandler(UIElement root, AttachmentInput input, Func<bool, Task> changeMode, Func<bool> canUse, Action<string> reportError)
    {
        this.root = root; this.input = input; this.changeMode = changeMode; this.canUse = canUse; this.reportError = reportError;
        root.PreviewKeyDown += PreviewKeyDown;
    }
    private static bool HasAttachment(DataPackageView data) => data.Contains(StandardDataFormats.Bitmap) || data.Contains(StandardDataFormats.StorageItems);
    private async void PreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.V || disposed || !canUse() ||
            !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
        try
        {
            var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!HasAttachment(data)) return; // The focused TextBox/PasswordBox keeps its normal text paste.
            args.Handled = true;
            await PasteAsync(data);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { if (!disposed && canUse()) reportError("Не удалось прочитать буфер обмена. Попробуйте ещё раз."); }
    }
    internal async Task<bool> PasteAsync(DataPackageView data)
    {
        if (disposed || !canUse() || !HasAttachment(data)) return false;
        if (busy) return true;
        busy = true;
        try
        {
            var images = data.Contains(StandardDataFormats.Bitmap);
            if (!images)
            {
                var items = await data.GetStorageItemsAsync();
                if (items.Count != 1 || items[0] is not StorageFile file)
                    throw new InvalidDataException("Вставьте один файл. Папки и несколько файлов в одном поле не поддерживаются.");
                images = new[] { ".png", ".jpg", ".jpeg" }.Contains(file.FileType.ToLowerInvariant());
            }
            if (disposed || !canUse()) return true;
            await changeMode(images);
            if (disposed || !canUse()) return true;
            await input.ReadDataAsync(data);
            if (!disposed && canUse()) reportError(input.ErrorMessage);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            AppDiagnostics.Journal.Record(AppEvent.ActionRejected, ex, "PasteAttachment");
            if (!disposed && canUse()) reportError(ex.Message);
        }
        finally { busy = false; }
        return true;
    }
    internal async Task PasteFromClipboardAsync()
    {
        if (disposed || !canUse()) return;
        try
        {
            var accepted = await PasteAsync(Windows.ApplicationModel.DataTransfer.Clipboard.GetContent());
            if (!accepted && !disposed && canUse()) reportError("В буфере нет изображения или файла. Скопируйте картинку либо файл в Проводнике.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { if (!disposed && canUse()) reportError("Не удалось прочитать буфер обмена. Попробуйте ещё раз."); }
    }
    public void Dispose() { disposed = true; root.PreviewKeyDown -= PreviewKeyDown; }
}
