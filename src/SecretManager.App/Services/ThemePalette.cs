using System;
using System.Globalization;
using SecretManager.Core.Models;

namespace SecretManager.App.Services;

// No XAML dependencies: the same palette and contrast rules are used by the app and tests.
public sealed record ThemePalette(bool IsLight, string Background, string Panel, string Card, string Control,
    string ControlHover, string Foreground, string Secondary, string Stroke, string Accent,
    string AccentHover, string AccentPressed, string OnAccent, string AccentText)
{
    public static ThemePalette Resolve(AppSettings settings, bool systemIsDark)
    {
        var background = settings.Theme switch
        {
            AppTheme.Light => "#F5F6FA", AppTheme.Dark => "#1C1C20",
            AppTheme.Midnight => "#11172A", AppTheme.Forest => "#14251F",
            AppTheme.Custom => settings.BackgroundColor,
            _ => systemIsDark ? "#1C1C20" : "#F5F6FA"
        };
        var light = Contrast("#000000", background) >= Contrast("#FFFFFF", background);
        var foreground = light ? "#172033" : "#F5F7FC";
        if (Contrast(foreground, background) < 4.5) foreground = light ? "#000000" : "#FFFFFF";
        string Surface(double amount) => EnsureSurfaceContrast(Mix(background, "#FFFFFF", amount), foreground, light);
        var panel = EnsureSurfaceContrast(Mix(background, light ? "#000000" : "#FFFFFF", .025), foreground, light);
        var card = Surface(light ? .65 : .055);
        var control = Surface(light ? .9 : .09);
        var hover = EnsureSurfaceContrast(Mix(control, light ? "#000000" : "#FFFFFF", .055), foreground, light);
        var accent = settings.Theme switch
        {
            AppTheme.Forest => "#58CFA2", AppTheme.Midnight => "#9DAAFF",
            AppTheme.Custom => settings.AccentColor, _ => light ? "#465FD1" : "#9AA9FF"
        };
        var onAccent = Contrast("#000000", accent) > Contrast("#FFFFFF", accent) ? "#000000" : "#FFFFFF";
        var accentHover = EnsureSurfaceContrast(Mix(accent, onAccent == "#FFFFFF" ? "#000000" : "#FFFFFF", .08), onAccent, onAccent == "#000000");
        var accentPressed = EnsureSurfaceContrast(Mix(accent, onAccent == "#FFFFFF" ? "#000000" : "#FFFFFF", .16), onAccent, onAccent == "#000000");
        var secondary = Mix(foreground, background, .24);
        var surfaces = new[] { background, panel, card, control, hover };
        if (Array.Exists(surfaces, surface => Contrast(secondary, surface) < 4.5)) secondary = foreground;
        var accentText = accent;
        for (var i = 0; Contrast(accentText, background) < 4.5 && i < 24; i++) accentText = Mix(accentText, foreground, .15);
        if (Contrast(accentText, background) < 4.5) accentText = foreground;
        return new(light, background, panel, card, control, hover, foreground, secondary,
            Mix(background, foreground, .22), accent, accentHover, accentPressed, onAccent, accentText);
    }

    private static string EnsureSurfaceContrast(string surface, string foreground, bool light)
    {
        for (var i = 0; Contrast(surface, foreground) < 4.5 && i < 24; i++) surface = Mix(surface, light ? "#FFFFFF" : "#000000", .08);
        return surface;
    }
    public static double Contrast(string a, string b)
    {
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
    private static double Luminance(string value)
    {
        var c = Components(value);
        static double Linear(int channel) { var s = channel / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
    }
    private static (int R, int G, int B) Components(string value) =>
        (int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber));
    private static string Mix(string a, string b, double amount)
    {
        var x = Components(a); var y = Components(b);
        return $"#{(int)Math.Round(x.R * (1 - amount) + y.R * amount):X2}{(int)Math.Round(x.G * (1 - amount) + y.G * amount):X2}{(int)Math.Round(x.B * (1 - amount) + y.B * amount):X2}";
    }
}
