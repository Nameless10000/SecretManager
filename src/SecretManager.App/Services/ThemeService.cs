using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using SecretManager.Core.Models;
using Windows.UI;

namespace SecretManager.App.Services;

public static class ThemeService
{
    public static Color ParseColor(string value) => Color.FromArgb(255,
        byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber), byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber),
        byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber));
    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    public static void Apply(Grid root, AppSettings settings)
    {
        var custom = settings.Theme is AppTheme.Midnight or AppTheme.Forest or AppTheme.Custom;
        var background = settings.Theme switch
        {
            AppTheme.Midnight => ParseColor("#11172A"),
            AppTheme.Forest => ParseColor("#14251F"),
            AppTheme.Custom => ParseColor(settings.BackgroundColor),
            AppTheme.Light => ParseColor("#F5F6FA"),
            _ => ParseColor("#1C1C20")
        };
        var accent = settings.Theme switch
        {
            AppTheme.Forest => ParseColor("#58CFA2"),
            AppTheme.Midnight => ParseColor("#9DAAFF"),
            AppTheme.Custom => ParseColor(settings.AccentColor),
            _ => ParseColor("#5978FF")
        };
        var light = custom ? Luminance(background) > 0.45 : settings.Theme == AppTheme.Light;
        root.RequestedTheme = settings.Theme == AppTheme.System ? ElementTheme.Default : light ? ElementTheme.Light : ElementTheme.Dark;
        if (settings.Theme == AppTheme.System)
        {
            light = root.ActualTheme == ElementTheme.Light;
            background = ParseColor(light ? "#F5F6FA" : "#1C1C20");
        }
        root.Background = new SolidColorBrush(custom ? background : Color.FromArgb(238, background.R, background.G, background.B));
        var card = Blend(background, light ? Colors.White : Colors.White, light ? .9 : .045);
        root.Resources["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(card);
        root.Resources["ExpanderHeaderBackground"] = new SolidColorBrush(card);
        root.Resources["ExpanderContentBackground"] = new SolidColorBrush(card);
        root.Resources["LayerFillColorDefaultBrush"] = new SolidColorBrush(Blend(background, light ? Colors.Black : Colors.White, .025));
        root.Resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(accent);
        root.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent);
        root.Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Blend(accent, background, .15));
        root.Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Blend(accent, background, .3));
        root.Resources["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(Luminance(accent) > .5 ? Colors.Black : Colors.White);
        ApplyTypography(root, settings);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, OriginalSize> originalSizes = new();
    private sealed record OriginalSize(double Value);
    public static void ApplyTypography(DependencyObject root, AppSettings settings)
    {
        if (root is Control control)
        {
            control.FontFamily = new FontFamily(settings.FontFamily);
            control.FontSize = settings.FontSize;
            if (control is Expander expander)
            {
                expander.CornerRadius = new CornerRadius(settings.CornerRadius);
                expander.Padding = new Thickness(settings.CompactLayout ? 10 : 18);
            }
        }
        if (root is TextBlock text)
        {
            text.FontFamily = new FontFamily(settings.FontFamily);
            var original = originalSizes.GetValue(text, t => new(t.FontSize)).Value;
            text.FontSize = original * settings.FontSize / 14;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ApplyTypography(VisualTreeHelper.GetChild(root, i), settings);
    }

    private static double Luminance(Color c) => .2126 * c.R / 255 + .7152 * c.G / 255 + .0722 * c.B / 255;
    private static Color Blend(Color a, Color b, double amount) => Color.FromArgb(255,
        (byte)(a.R * (1 - amount) + b.R * amount), (byte)(a.G * (1 - amount) + b.G * amount), (byte)(a.B * (1 - amount) + b.B * amount));
}
