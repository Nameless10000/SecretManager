using SecretManager.App.Services;

namespace SecretManager.App.Controls;

public sealed class HotkeySettingsControl : UserControl
{
    private readonly CheckBox enabled = new() { Content = "Включить быстрый доступ" };
    private readonly CheckBox control = new() { Content = "Ctrl" };
    private readonly CheckBox alt = new() { Content = "Alt" };
    private readonly CheckBox shift = new() { Content = "Shift" };
    private readonly CheckBox windows = new() { Content = "Win" };
    private readonly ComboBox key = new() { Header = "Клавиша", DisplayMemberPath = "Value", HorizontalAlignment = HorizontalAlignment.Stretch };
    public HotkeySettingsControl()
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(enabled);
        var modifiers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var modifier in new[] { control, alt, shift, windows })
        {
            modifier.MinWidth = 0;
            modifiers.Children.Add(modifier);
        }

        body.Children.Add(modifiers);
        body.Children.Add(key);
        var keys = Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x70, 24))
            .Select(code => new KeyValuePair<uint, string>((uint)code, code >= 0x70 ? "F" + (code - 0x6F) : ((char)code).ToString())).ToArray();
        key.ItemsSource = keys;
        Content = body;
    }

    public void Load(HotkeyGesture gesture)
    {
        enabled.IsChecked = gesture.Enabled;
        control.IsChecked = (gesture.Modifiers & 2) != 0;
        alt.IsChecked = (gesture.Modifiers & 1) != 0;
        shift.IsChecked = (gesture.Modifiers & 4) != 0;
        windows.IsChecked = (gesture.Modifiers & 8) != 0;
        key.SelectedItem = ((IEnumerable<KeyValuePair<uint, string>>)key.ItemsSource).Single(item => item.Key == gesture.Key);
    }

    public HotkeyGesture Capture()
    {
        var gesture = new HotkeyGesture(key.SelectedItem is KeyValuePair<uint, string> selected ? selected.Key : 0,
            (control.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) |
            (shift.IsChecked == true ? 4u : 0) | (windows.IsChecked == true ? 8u : 0), enabled.IsChecked == true);
        if (!gesture.IsValid && gesture.Enabled)
        {
            throw new ArgumentException("Выберите клавишу и хотя бы один модификатор Ctrl, Alt или Win.");
        }

        return gesture.IsValid ? gesture : new HotkeyGesture(Enabled: false);
    }
}
