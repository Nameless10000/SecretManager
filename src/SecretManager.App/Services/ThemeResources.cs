using Microsoft.UI.Xaml.Media;
using SecretManager.Core.Models;

namespace SecretManager.App.Services;

// Created by XAML BEFORE its children. Keep these brush instances for the window's lifetime:
// replacing ResourceDictionary entries does not update brushes already held by controls.
public sealed class ThemeResources : ResourceDictionary
{
    public ThemeResources()
    {
        Brush("VaultBackgroundBrush", "ApplicationPageBackgroundThemeBrush", "SolidBackgroundFillColorBaseBrush");
        Brush("VaultPanelBrush", "LayerFillColorDefaultBrush");
        Brush("VaultCardBrush", "CardBackgroundFillColorDefaultBrush", "ExpanderHeaderBackground", "ExpanderContentBackground",
            "ContentDialogBackground", "FlyoutPresenterBackground", "ComboBoxDropDownBackground");
        Brush("VaultControlBrush", "ButtonBackground", "ComboBoxBackground", "ComboBoxBackgroundUnfocused", "TextControlBackground");
        Brush("VaultControlHoverBrush", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ComboBoxBackgroundPointerOver",
            "ComboBoxBackgroundPressed", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused");
        Brush("VaultTextBrush", "TextFillColorPrimaryBrush", "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
            "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxForegroundPointerOver",
            "ComboBoxForegroundPressed", "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "ContentDialogForeground");
        Brush("VaultSecondaryTextBrush", "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush");
        Brush("VaultStrokeBrush", "CardStrokeColorDefaultBrush", "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed", "ComboBoxDropDownBorderBrush",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "ExpanderHeaderBorderBrush", "ExpanderContentBorderBrush");
        Brush("VaultAccentBrush", "AccentFillColorDefaultBrush", "AccentButtonBackground", "SliderThumbBackground", "SliderTrackValueFill",
            "ToggleSwitchFillOn", "TextControlBorderBrushFocused", "ComboBoxBackgroundBorderBrushFocused");
        Brush("VaultAccentHoverBrush", "AccentFillColorSecondaryBrush", "AccentButtonBackgroundPointerOver", "SliderThumbBackgroundPointerOver",
            "SliderTrackValueFillPointerOver", "ToggleSwitchFillOnPointerOver");
        Brush("VaultAccentPressedBrush", "AccentFillColorTertiaryBrush", "AccentButtonBackgroundPressed", "SliderThumbBackgroundPressed",
            "SliderTrackValueFillPressed", "ToggleSwitchFillOnPressed");
        Brush("VaultOnAccentBrush", "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "AccentButtonForeground",
            "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed");
        Brush("VaultAccentTextBrush", "SystemControlHighlightAccentBrush");
        Apply(ThemePalette.Resolve(new AppSettings(), Microsoft.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Dark));
    }
    private void Brush(string name, params string[] aliases)
    {
        var brush = new SolidColorBrush();
        this[name] = brush;
        foreach (var alias in aliases) this[alias] = brush;
    }
    public void Apply(ThemePalette p)
    {
        Set("VaultBackgroundBrush", p.Background); Set("VaultPanelBrush", p.Panel); Set("VaultCardBrush", p.Card);
        Set("VaultControlBrush", p.Control); Set("VaultControlHoverBrush", p.ControlHover);
        Set("VaultTextBrush", p.Foreground); Set("VaultSecondaryTextBrush", p.Secondary); Set("VaultStrokeBrush", p.Stroke);
        Set("VaultAccentBrush", p.Accent); Set("VaultAccentHoverBrush", p.AccentHover); Set("VaultAccentPressedBrush", p.AccentPressed);
        Set("VaultOnAccentBrush", p.OnAccent); Set("VaultAccentTextBrush", p.AccentText);
    }
    private void Set(string name, string color) => ((SolidColorBrush)this[name]).Color = ThemeService.ParseColor(color);
}
