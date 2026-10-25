using System.Globalization;
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
    public static ThemePalette Apply(Grid root, AppSettings settings)
    {
        if (root.Resources is not ThemeResources resources) throw new InvalidOperationException("Window must declare ThemeResources before loading its controls.");
        if (settings.Theme == AppTheme.System) root.RequestedTheme = ElementTheme.Default;
        var palette = ThemePalette.Resolve(settings, root.ActualTheme == ElementTheme.Dark);
        // Update shared brushes first, then re-theme native controls. Both directions and
        // Dark -> Midnight -> Forest (same ElementTheme) must update existing visuals.
        resources.Apply(palette);
        if (settings.Theme != AppTheme.System) root.RequestedTheme = palette.IsLight ? ElementTheme.Light : ElementTheme.Dark;
        root.Background = (SolidColorBrush)resources["VaultBackgroundBrush"];
        ApplyTypography(root, settings);
        return palette;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, OriginalSize> originalSizes = new();
    private sealed record OriginalSize(double Value);
    public static void ApplyTypography(DependencyObject root, AppSettings settings)
    {
        if (root is IconElement) return; // Never replace the font inside FontIcon / SymbolIcon / AnimatedIcon.
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
        if (root is TextBlock text && !IsSymbolFont(text.FontFamily.Source))
        {
            text.FontFamily = new FontFamily(settings.FontFamily);
            var original = originalSizes.GetValue(text, t => new(t.FontSize)).Value;
            text.FontSize = original * settings.FontSize / 14;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ApplyTypography(VisualTreeHelper.GetChild(root, i), settings);
    }

    private static bool IsSymbolFont(string family) => family.Contains("Icons", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("MDL2", StringComparison.OrdinalIgnoreCase) || family.Contains("Symbol", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("Wingdings", StringComparison.OrdinalIgnoreCase) || family.Contains("Webdings", StringComparison.OrdinalIgnoreCase);
}
