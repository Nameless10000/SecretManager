using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SecretManager.App.Services;
using SecretManager.Core.Models;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace SecretManager.ThemeChecks;

public sealed partial class ThemeCheckWindow : Window
{
    private readonly string output = Environment.GetEnvironmentVariable("SECRETMANAGER_THEME_CHECK_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "theme-checks");
    public ThemeCheckWindow()
    {
        InitializeComponent(); AppWindow.Resize(new SizeInt32(1400, 900));
        Root.Loaded += async (_, _) => await RunChecks();
    }
    private async Task RunChecks()
    {
        Directory.CreateDirectory(output);
        try
        {
            var cases = new (string Name, AppSettings Settings)[]
            {
                ("light", new() { Theme = AppTheme.Light }), ("dark", new() { Theme = AppTheme.Dark }),
                ("midnight", new() { Theme = AppTheme.Midnight }), ("forest", new() { Theme = AppTheme.Forest }),
                ("custom-light", new() { Theme = AppTheme.Custom, BackgroundColor = "#FFF4E5", AccentColor = "#006C6A", FontFamily = "Consolas" }),
                ("custom-dark", new() { Theme = AppTheme.Custom, BackgroundColor = "#19222E", AccentColor = "#FFC66D" }),
                ("custom-gray", new() { Theme = AppTheme.Custom, BackgroundColor = "#777777", AccentColor = "#777777" }),
                ("system", new() { Theme = AppTheme.System })
            };
            var shared = (ThemeResources)Root.Resources;
            var panelBrush = (SolidColorBrush)shared["VaultPanelBrush"];
            var accentBrush = (SolidColorBrush)shared["VaultAccentBrush"];
            var font = SemanticIcon.FontFamily.Source;
            var count = 0;
            foreach (var before in cases)
            {
                ThemeService.Apply(Root, before.Settings); Root.UpdateLayout(); await NextFrame();
                foreach (var after in cases)
                {
                    var p = ThemeService.Apply(Root, after.Settings);
                    Root.UpdateLayout(); await NextFrame();
                    Check(ReferenceEquals(panelBrush, Sidebar.Background), "Sidebar lost its shared brush reference.");
                    Check(ReferenceEquals(panelBrush, SettingsPanel.Background), "Settings lost its shared brush reference.");
                    Check(ThemeService.ToHex(panelBrush.Color) == p.Panel, $"Stale panel after {before.Name} -> {after.Name}");
                    Check(ThemeService.ToHex(((SolidColorBrush)AccentButton.Background).Color) == p.Accent, "Native accent button still uses the system accent.");
                    Check(ThemeService.ToHex(((SolidColorBrush)AccentButton.Foreground).Color) == p.OnAccent, "Native accent label has the wrong foreground.");
                    Check(ReferenceEquals(accentBrush, shared["AccentButtonBackground"]), "Native accent aliases were replaced.");
                    Check(SemanticIcon.FontFamily.Source == font, "Icon font was overwritten.");
                    var symbolTexts = Descendants(Root).OfType<TextBlock>()
                        .Where(t => t.Text.Any(c => c is >= '\uE000' and <= '\uF8FF')).ToArray();
                    Check(symbolTexts.All(t => t.FontFamily.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase) ||
                        t.FontFamily.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase) || t.FontFamily.Source.Contains("Symbol", StringComparison.OrdinalIgnoreCase)),
                        "A control's internal symbol font was overwritten: " + string.Join(", ", symbolTexts.Select(t => t.FontFamily.Source)));
                    var growth = (MotionGeometry.CardHoverScale(Card.ActualWidth) - 1) * Card.ActualWidth / 2;
                    Check(growth * 1.3 + 2 < CardList.Padding.Left, "Hover card extends beyond the reserved gutter.");
                    count++;
                }
            }
            foreach (var item in cases)
            {
                ThemeService.Apply(Root, item.Settings); ThemeName.Text = "Ваши секреты · " + item.Name;
                Root.UpdateLayout(); await NextFrame(); await Snapshot(item.Name + ".png");
            }
            var initialTop = CardList.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y;
            var initialHeight = CardList.ActualHeight;
            Toast.Show("Настройки сохранены", false); Root.UpdateLayout(); await NextFrame();
            Check(CardList.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y == initialTop && CardList.ActualHeight == initialHeight, "Toast shifts or resizes the list.");
            Check(Toast.ActualHeight < 70, "Toast is not compact.");
            await Snapshot("toast-overlay.png");
            await Task.Delay(2200);
            Check(!Toast.IsOpen && Toast.Message == "", "Toast did not dismiss after two seconds.");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { Passed = true, ThemeTransitions = count, Snapshots = cases.Length + 1, NativeControls = true, IconFonts = true, HoverGutters = true, ToastOverlay = true, ToastAutoDismiss = true }));
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            try { await Snapshot("failure.png"); } catch { }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() }));
            Environment.ExitCode = 1;
        }
        finally { Close(); Microsoft.UI.Xaml.Application.Current.Exit(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        {
            var child = VisualTreeHelper.GetChild(item, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Task NextFrame()
    {
        var ready = new TaskCompletionSource();
        EventHandler<object>? handler = null;
        handler = (_, _) => { CompositionTarget.Rendering -= handler; ready.TrySetResult(); };
        CompositionTarget.Rendering += handler;
        return ready.Task;
    }
    private async Task Snapshot(string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(Root);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var folder = await StorageFolder.GetFolderFromPathAsync(output);
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
