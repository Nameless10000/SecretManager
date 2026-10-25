using SecretManager.App.Services;
using SecretManager.Core.Models;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class AppearanceTests
{
    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Midnight)]
    [InlineData(AppTheme.Forest)]
    [InlineData(AppTheme.System)]
    public void PresetTextHasReadableContrastInBothSystemModes(AppTheme theme)
    {
        foreach (var dark in new[] { false, true }) CheckContrast(ThemePalette.Resolve(new() { Theme = theme }, dark));
    }

    [Theory]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("#000000", "#000000")]
    [InlineData("#777777", "#777777")]
    [InlineData("#707070", "#007000")]
    [InlineData("#FFF4E5", "#006C6A")]
    [InlineData("#19222E", "#FFC66D")]
    [InlineData("#00FF00", "#FFFF00")]
    public void CustomThemeChoosesReadableTextEvenForSimilarColors(string background, string accent) =>
        CheckContrast(ThemePalette.Resolve(new() { Theme = AppTheme.Custom, BackgroundColor = background, AccentColor = accent }, true));

    private static void CheckContrast(ThemePalette p)
    {
        foreach (var surface in new[] { p.Background, p.Panel, p.Card, p.Control, p.ControlHover })
        {
            Assert.True(ThemePalette.Contrast(p.Foreground, surface) >= 4.5, $"Primary: {p.Foreground}/{surface}");
            Assert.True(ThemePalette.Contrast(p.Secondary, surface) >= 4.5, $"Secondary: {p.Secondary}/{surface}");
        }
        foreach (var fill in new[] { p.Accent, p.AccentHover, p.AccentPressed })
            Assert.True(ThemePalette.Contrast(p.OnAccent, fill) >= 4.5, $"Accent label: {p.OnAccent}/{fill}");
        Assert.True(ThemePalette.Contrast(p.AccentText, p.Background) >= 4.5);
    }

    [Fact]
    public void SystemPaletteFollowsSystemModeWithoutRememberingThePreviousPreset()
    {
        var settings = new AppSettings { Theme = AppTheme.System };
        var dark = ThemePalette.Resolve(settings, true);
        var light = ThemePalette.Resolve(settings, false);
        Assert.False(dark.IsLight); Assert.True(light.IsLight);
        Assert.NotEqual(dark.Panel, light.Panel);
        Assert.Equal(ThemePalette.Resolve(new() { Theme = AppTheme.Dark }, false), dark);
        Assert.Equal(ThemePalette.Resolve(new() { Theme = AppTheme.Light }, true), light);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(600)]
    [InlineData(1200)]
    [InlineData(3840)]
    [InlineData(7680)]
    public void CardHoverGrowthIncludingSpringOvershootFitsTheViewportGutter(double width)
    {
        var scale = MotionGeometry.CardHoverScale(width);
        Assert.InRange(scale, 1.000001, 1.012001);
        var growthPerSide = (scale - 1) * width / 2;
        Assert.True(growthPerSide * 1.3 + 2 < MotionGeometry.CardGutter);
    }
}
