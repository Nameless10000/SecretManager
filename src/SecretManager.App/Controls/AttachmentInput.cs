using System.Security.Cryptography;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SecretManager.App.Services;
using SecretManager.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace SecretManager.App.Controls;

public sealed class AttachmentInput : UserControl, IDisposable
{
    private readonly AttachmentImportService importer = new();
    private readonly Border dropZone;
    private readonly TextBlock heading = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock details = new() { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock fileName = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed) };
    private readonly Microsoft.UI.Xaml.Controls.Image preview = new() { MaxHeight = 120, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
    private readonly Button paste = new() { Content = "Вставить из буфера · Ctrl+V" };
    private readonly Button pick = new() { Content = "Выбрать файл…" };
    private readonly SolidColorBrush borderBrush = new();
    private readonly SolidColorBrush accentBrush = new();
    private Func<bool> isCurrent = () => true;
    private int revision;
    private bool disposed;
    public bool ImagesOnly { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsValidForCurrentMode { get; private set; }
    public AttachmentContent? Attachment { get; private set; }
    public string ErrorMessage => error.Text;
    public event Action? PickRequested;
    public event Action? PasteRequested;
    public event Action? StateChanged;

    public AttachmentInput()
    {
        var panel = new StackPanel { Spacing = 12 };
        var body = new StackPanel { Spacing = 8, IsHitTestVisible = false };
        body.Children.Add(new FontIcon { Glyph = "\uE898", FontSize = 28 }); body.Children.Add(heading); body.Children.Add(details); body.Children.Add(preview);
        dropZone = new Border { Child = body, Padding = new Thickness(18), MinHeight = 130, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), AllowDrop = true, BorderBrush = borderBrush };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(pick); actions.Children.Add(paste);
        panel.Children.Add(dropZone); panel.Children.Add(actions); panel.Children.Add(fileName); panel.Children.Add(error); Content = panel;
        AllowDrop = true;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            AllowDrop = dropZone.AllowDrop = false;
            panel.Children.Add(new TextBlock { Text = "Windows ограничивает перетаскивание при запуске от администратора. Используйте «Выбрать файл» или запустите приложение обычным способом.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        pick.Click += (_, _) => PickRequested?.Invoke();
        paste.Click += async (_, _) => { if (PasteRequested is { } requested) requested(); else await PasteFromClipboardAsync(); };
        DragOver += (_, e) =>
        {
            var accepted = !disposed && isCurrent() && (e.DataView.Contains(StandardDataFormats.StorageItems) || ImagesOnly && e.DataView.Contains(StandardDataFormats.Bitmap));
            e.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
            e.Handled = true;
            if (accepted) { dropZone.BorderBrush = accentBrush; e.DragUIOverride.Caption = ImagesOnly ? "Добавить изображение" : "Добавить файл"; e.DragUIOverride.IsCaptionVisible = true; }
        };
        DragLeave += (_, _) => dropZone.BorderBrush = borderBrush;
        Drop += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try { e.Handled = true; await ReadDataAsync(e.DataView); e.AcceptedOperation = ErrorMessage.Length == 0 && IsValidForCurrentMode ? DataPackageOperation.Copy : DataPackageOperation.None; }
            finally { dropZone.BorderBrush = borderBrush; deferral.Complete(); }
        };
    }

    public void Configure(AppSettings settings, bool systemIsDark, Func<bool> canUse)
    {
        isCurrent = canUse;
        var palette = ThemePalette.Resolve(settings, systemIsDark);
        borderBrush.Color = ThemeService.ParseColor(palette.Stroke); accentBrush.Color = ThemeService.ParseColor(palette.AccentText);
        dropZone.Background = new SolidColorBrush(ThemeService.ParseColor(palette.Control));
        details.Foreground = new SolidColorBrush(ThemeService.ParseColor(palette.Secondary));
        fileName.Foreground = new SolidColorBrush(ThemeService.ParseColor(palette.Secondary));
        RequestedTheme = palette.IsLight ? ElementTheme.Light : ElementTheme.Dark;
        FontFamily = new FontFamily(settings.FontFamily); FontSize = settings.FontSize;
    }

    public async Task SetImageModeAsync(bool imagesOnly)
    {
        revision++; IsBusy = false; pick.IsEnabled = paste.IsEnabled = true; ImagesOnly = imagesOnly;
        heading.Text = imagesOnly ? "Перетащите сюда изображение" : "Перетащите сюда файл";
        details.Text = imagesOnly ? "PNG / JPEG до 10 МБ · также можно вставить из буфера" : "Один файл до 10 МБ · можно скопировать в Проводнике и вставить сюда";
        paste.Visibility = Visibility.Visible;
        preview.Visibility = imagesOnly && preview.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        error.Text = ""; IsValidForCurrentMode = Attachment is not null && !imagesOnly;
        StateChanged?.Invoke();
        if (Attachment is { } existing && imagesOnly) await LoadAsync(() => importer.ValidateExistingAsync(existing, true));
    }
    public Task SetExistingAsync(SecretField field)
    {
        var bytes = Convert.FromBase64String(field.Value);
        try
        {
            var content = AttachmentContent.FromBytes(field.FileName!, bytes, ImagesOnly, field.ContentType);
            return LoadAsync(() => importer.ValidateExistingAsync(content, ImagesOnly));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public Task SetFileAsync(StorageFile file) => LoadAsync(() => importer.FromFileAsync(file, ImagesOnly));
    public Task SetPathAsync(string path) => LoadAsync(() => importer.FromPathAsync(path, ImagesOnly));
    public Task ReadDataAsync(DataPackageView data) => LoadAsync(() => importer.FromDataAsync(data, ImagesOnly));
    public Task PasteFromClipboardAsync() => LoadAsync(() => importer.FromDataAsync(Windows.ApplicationModel.DataTransfer.Clipboard.GetContent(), ImagesOnly));

    private async Task LoadAsync(Func<Task<AttachmentContent>> read)
    {
        if (disposed || !isCurrent()) return;
        var ticket = ++revision; IsBusy = true; pick.IsEnabled = paste.IsEnabled = false; error.Text = ""; StateChanged?.Invoke();
        bool Current() => !disposed && ticket == revision && isCurrent();
        try
        {
            var candidate = await read();
            if (!Current()) return;
            BitmapImage? bitmap = null;
            if (ImagesOnly)
            {
                var bytes = Convert.FromBase64String(candidate.Base64);
                try
                {
                    using var stream = await AttachmentImportService.InMemoryAsync(bytes);
                    var metadata = await BitmapDecoder.CreateAsync(stream);
                    var scale = Math.Min(1, Math.Min(420d / metadata.PixelWidth, 120d / metadata.PixelHeight));
                    bitmap = new BitmapImage { DecodePixelWidth = Math.Max(1, (int)Math.Round(metadata.PixelWidth * scale)),
                        DecodePixelHeight = Math.Max(1, (int)Math.Round(metadata.PixelHeight * scale)) };
                    stream.Seek(0); await bitmap.SetSourceAsync(stream);
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            if (!Current()) return;
            Attachment = candidate; IsValidForCurrentMode = true;
            var size = candidate.Length < 1024 ? $"{candidate.Length} Б" : $"{candidate.Length / 1024.0:F1} КБ";
            fileName.Text = $"{candidate.FileName} · {size}";
            preview.Source = bitmap; preview.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or
            System.Runtime.InteropServices.COMException or InvalidOperationException or FormatException)
        { if (Current()) error.Text = ex.Message; }
        finally
        {
            if (!disposed && ticket == revision) { IsBusy = false; pick.IsEnabled = paste.IsEnabled = true; StateChanged?.Invoke(); }
        }
    }
    public void Dispose()
    {
        disposed = true; revision++; IsBusy = false; Attachment = null; IsValidForCurrentMode = false;
        preview.Source = null; fileName.Text = ""; error.Text = "";
    }
}
