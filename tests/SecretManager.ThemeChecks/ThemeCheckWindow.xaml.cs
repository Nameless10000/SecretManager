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
        Closed += (_, _) => File.WriteAllText(Path.Combine(output, "teardown-stage.txt"), "Window.Closed");
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
            var nativeTimings = Descendants(Root).OfType<FrameworkElement>()
                .SelectMany(VisualStateManager.GetVisualStateGroups).SelectMany(g => g.States)
                .Select(s => s.Storyboard).OfType<Microsoft.UI.Xaml.Media.Animation.Storyboard>().Distinct()
                .ToDictionary(s => s, s => (s.Duration, s.SpeedRatio));
            Check(nativeTimings.Count > 0, "Native visual-state timelines were not found.");
            var toggleChecks = await CheckToggleScenarios();
            var motionSettings = new AppSettings { AnimationLevel = AnimationLevel.Maximum };
            var motion = new MotionService(() => motionSettings, () => true);
            motion.Wire(Root);
            var iconSurface = Descendants(AccentButton).OfType<ContentPresenter>().First();
            Check(motion.Enabled ? iconSurface.BackgroundTransition?.Duration == TimeSpan.FromMilliseconds(180) : iconSurface.BackgroundTransition is null, "Hover background transition ignores the system motion setting.");
            var expanderContent = Descendants(Card).OfType<Border>().Single(b => b.Name == "ExpanderContent");
            foreach (var speed in new[] { .5, 1, 2 })
            {
                motionSettings = motionSettings with { MotionSpeed = speed }; motion.Wire(Root);
                Check(nativeTimings.All(p => p.Key.SpeedRatio == p.Value.SpeedRatio), "Decorative speed changes a standard control's timing.");
                Card.IsExpanded = false;
                await Task.Delay(450);
                Check(expanderContent.Visibility == Visibility.Collapsed, $"Card is still collapsing at decorative speed {speed}.");
                Card.IsExpanded = true;
                await Task.Delay(450);
                Check(expanderContent.Visibility == Visibility.Visible && expanderContent.RenderTransform is CompositeTransform transform &&
                    Math.Abs(transform.TranslateY) < .5, $"Card is still expanding at decorative speed {speed}.");
            }
            motionSettings = motionSettings with { AnimationLevel = AnimationLevel.Off }; motion.Wire(Root);
            Check(iconSurface.BackgroundTransition is null, "Disabled animations still transition the hover background.");
            Check(nativeTimings.All(p => p.Key.Duration.Equals(p.Value.Duration) && p.Key.SpeedRatio == p.Value.SpeedRatio),
                "Disabling effects must preserve standard control state timelines.");
            motionSettings = motionSettings with { AnimationLevel = AnimationLevel.Medium, MotionSpeed = 2 }; motion.Wire(Root);
            Check(motion.Enabled ? iconSurface.BackgroundTransition?.Duration == TimeSpan.FromMilliseconds(60) : iconSurface.BackgroundTransition is null, "Hover transition ignores animation speed or system settings.");
            Check(nativeTimings.All(p => p.Key.Duration.Equals(p.Value.Duration) && p.Key.SpeedRatio == p.Value.SpeedRatio),
                "Changing decorative speed changes standard control timing.");
            var noSystemMotion = new MotionService(() => motionSettings, () => false);
            noSystemMotion.Wire(Root);
            Check(!noSystemMotion.Enabled && iconSurface.BackgroundTransition is null, "Reduced system motion must disable custom animation.");
            Check(nativeTimings.All(p => p.Key.Duration.Equals(p.Value.Duration) && p.Key.SpeedRatio == p.Value.SpeedRatio),
                "Reduced system motion must preserve standard control state timelines.");
            var attachmentChecks = await AttachmentChecks.RunAsync(Card, output, Snapshot);
            // Detach decoded images before tearing down the XAML/composition tree.
            Card.Content = null; Root.UpdateLayout(); await NextFrame(); await NextFrame();
            var textInput = Descendants(Root).OfType<TextBox>().First();
            textInput.Focus(FocusState.Programmatic);
            Check(AccentButton.Focus(FocusState.Programmatic), "Could not clear text-input focus before WinUI shutdown.");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { Passed = true, ThemeTransitions = count, Snapshots = cases.Length + 4, NativeControls = true, IconFonts = true, HoverGutters = true,
                ToastOverlay = true, ToastAutoDismiss = true, HoverBrushTransitions = true, FixedControlTimings = true, ExpanderTransitions = 6,
                ToggleChecks = toggleChecks, SystemMotionDisabled = true, AttachmentChecks = attachmentChecks }));
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            try { await Snapshot("failure.png"); } catch { }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() }));
            Environment.ExitCode = 1;
        }
        finally
        {
            // Let the async Loaded handler complete before destroying its dispatcher/XAML island.
            File.WriteAllText(Path.Combine(output, "teardown-stage.txt"), "Close queued");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                AccentButton.Focus(FocusState.Programmatic);
                File.WriteAllText(Path.Combine(output, "teardown-stage.txt"), "Before Window.Close");
                Close();
                File.WriteAllText(Path.Combine(output, "teardown-stage.txt"), "Window.Close returned");
            });
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private async Task<int> CheckToggleScenarios()
    {
        var settings = new AppSettings();
        var systemMotion = true;
        var motion = new MotionService(() => settings, () => systemMotion);
        var applying = false;
        void Apply()
        {
            if (applying) return;
            applying = true;
            try { ThemeService.Apply(Root, settings); motion.Reset(Root); motion.Wire(Root); }
            finally { applying = false; }
        }
        // Appearance preview runs synchronously from CompactChoice.Toggled in the
        // real settings panel, while WinUI is still updating its visual states.
        void Preview(object sender, RoutedEventArgs args) => Apply();
        CompactToggle.Toggled += Preview;
        var count = 0;
        try
        {
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            foreach (var level in new[] { AnimationLevel.Off, AnimationLevel.Medium, AnimationLevel.Maximum })
            foreach (var speed in new[] { .5, 1, 2 })
            {
                systemMotion = speed != 1;
                settings = new() { Theme = theme, AnimationLevel = level, MotionSpeed = speed };
                Apply();
                SettingsPanel.Visibility = Visibility.Collapsed;
                CompactToggle.IsOn = false; AccordionToggle.IsOn = true;
                SettingsPanel.Visibility = Visibility.Visible; Root.UpdateLayout(); await Task.Delay(220);
                CheckToggle(CompactToggle); CheckToggle(AccordionToggle); count += 2;
                CompactToggle.IsOn = true; AccordionToggle.IsOn = false;
                await Task.Delay(220);
                CheckToggle(CompactToggle); CheckToggle(AccordionToggle); count += 2;
            }
            await Snapshot("toggle-switches.png");
            return count;
        }
        finally { CompactToggle.Toggled -= Preview; ThemeService.Apply(Root, new()); }
    }
    private static void CheckToggle(ToggleSwitch control)
    {
        var visuals = Descendants(control).OfType<FrameworkElement>().Where(e => e.Name.Length > 0)
            .GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var onLabel = visuals["OnContentPresenter"]; var offLabel = visuals["OffContentPresenter"];
        var onKnob = visuals["SwitchKnobOn"]; var offKnob = visuals["SwitchKnobOff"];
        Check(control.IsOn ? onLabel.Opacity > .99 && offLabel.Opacity < .01 : offLabel.Opacity > .99 && onLabel.Opacity < .01,
            $"Toggle {control.Header} shows overlapping/wrong labels (IsOn={control.IsOn}, on={onLabel.Opacity}, off={offLabel.Opacity}).");
        Check(control.IsOn ? onKnob.Opacity > .99 && offKnob.Opacity < .01 : offKnob.Opacity > .99 && onKnob.Opacity < .01,
            $"Toggle {control.Header} shows overlapping/wrong knobs.");
    }
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
