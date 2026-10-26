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

    private sealed class TypographyState : DependencyObject
    {
        internal static readonly DependencyProperty OriginalSizeProperty = DependencyProperty.RegisterAttached(
            "OriginalFontSize", typeof(double), typeof(TypographyState), new PropertyMetadata(double.NaN));
    }
    public static void ApplyTypography(DependencyObject root, AppSettings settings)
    {
        CaptureOriginalSizes(root);
        ApplyTypographyCore(root, settings);
    }
    private static void CaptureOriginalSizes(DependencyObject root)
    {
        if (root is IconElement) return;
        if (root is TextBlock text && double.IsNaN((double)text.GetValue(TypographyState.OriginalSizeProperty)) &&
            text.ReadLocalValue(TextBlock.FontSizeProperty) is double size)
            text.SetValue(TypographyState.OriginalSizeProperty, size);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) CaptureOriginalSizes(VisualTreeHelper.GetChild(root, i));
    }
    private static void ApplyTypographyCore(DependencyObject root, AppSettings settings)
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
            // Keep the baseline on the native object: WinRT wrappers may be collected
            // and recreated while the visual is still alive. Inherited/bound text
            // keeps inheriting from its Control instead of being scaled a second time.
            var original = (double)text.GetValue(TypographyState.OriginalSizeProperty);
            if (double.IsFinite(original)) text.FontSize = original * settings.FontSize / 14;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) ApplyTypographyCore(VisualTreeHelper.GetChild(root, i), settings);
    }

    private static bool IsSymbolFont(string family) => family.Contains("Icons", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("MDL2", StringComparison.OrdinalIgnoreCase) || family.Contains("Symbol", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("Wingdings", StringComparison.OrdinalIgnoreCase) || family.Contains("Webdings", StringComparison.OrdinalIgnoreCase);
}
