using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using Forms = System.Windows.Forms;

namespace SecretManager.App.Services;

internal static class FilePickerService
{
    internal static async Task<string?> OpenAsync(WindowId window, nint hwnd, string title, params string[] extensions)
    {
        try
        {
            var picker = new FileOpenPicker(window);
            foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
            return (await picker.PickSingleFileAsync())?.Path;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppDiagnostics.Journal.Record(AppEvent.ActionRejected, ex);
            using var fallback = new Forms.OpenFileDialog { Title = title, Multiselect = false,
                Filter = Filter(extensions), CheckFileExists = true, RestoreDirectory = true };
            return fallback.ShowDialog(new Owner(hwnd)) == Forms.DialogResult.OK ? fallback.FileName : null;
        }
    }

    internal static async Task<string?> SaveAsync(WindowId window, nint hwnd)
    {
        try
        {
            var picker = new FileSavePicker(window) { SuggestedFileName = "SecretManager-backup", DefaultFileExtension = ".smvault" };
            picker.FileTypeChoices.Add("Зашифрованное хранилище", [".smvault"]);
            return (await picker.PickSaveFileAsync())?.Path;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppDiagnostics.Journal.Record(AppEvent.ActionRejected, ex);
            using var fallback = new Forms.SaveFileDialog { Title = "Экспорт хранилища", FileName = "SecretManager-backup.smvault",
                DefaultExt = "smvault", Filter = Filter([".smvault"]), AddExtension = true, OverwritePrompt = true, RestoreDirectory = true };
            return fallback.ShowDialog(new Owner(hwnd)) == Forms.DialogResult.OK ? fallback.FileName : null;
        }
    }
    private static string Filter(string[] extensions) => extensions.Contains("*") ? "Все файлы|*.*" :
        "Поддерживаемые файлы|" + string.Join(";", extensions.Select(e => "*" + e));
    private sealed class Owner(nint hwnd) : Forms.IWin32Window { public nint Handle => hwnd; }
}
