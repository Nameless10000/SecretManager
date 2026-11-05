using SecretManager.Core.Models;
using SecretManager.Core.Services;

namespace SecretManager.App.Controls;

public sealed class PasswordGeneratorSettingsControl : UserControl
{
    private readonly NumberBox length = new() { Header = "Длина пароля", Minimum = 4, Maximum = 128, Value = 24, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly CheckBox lowercase = new() { Content = "Строчные буквы a–z", IsChecked = true };
    private readonly CheckBox uppercase = new() { Content = "Заглавные буквы A–Z", IsChecked = true };
    private readonly CheckBox digits = new() { Content = "Цифры 0–9", IsChecked = true };
    private readonly CheckBox symbols = new() { Content = "Спецсимволы", IsChecked = true };
    private readonly CheckBox similar = new() { Content = "Исключить похожие символы", IsChecked = true };

    public PasswordGeneratorSettingsControl()
    {
        var options = new StackPanel { Spacing = 6 };
        options.Children.Add(length);
        options.Children.Add(lowercase);
        options.Children.Add(uppercase);
        options.Children.Add(digits);
        options.Children.Add(symbols);
        options.Children.Add(similar);
        Content = options;
    }

    public PasswordGeneratorOptions Capture()
    {
        if (!double.IsFinite(length.Value) || length.Value != Math.Truncate(length.Value))
        {
            throw new ArgumentException("Укажите целую длину от 4 до 128.");
        }

        var value = new PasswordGeneratorOptions { Length = (int)length.Value, Lowercase = lowercase.IsChecked == true, Uppercase = uppercase.IsChecked == true, Digits = digits.IsChecked == true, Symbols = symbols.IsChecked == true, ExcludeSimilar = similar.IsChecked == true };
        PasswordGenerator.ValidateOptions(value);
        return value;
    }

    public void Load(PasswordGeneratorOptions value)
    {
        length.Value = value.Length;
        lowercase.IsChecked = value.Lowercase;
        uppercase.IsChecked = value.Uppercase;
        digits.IsChecked = value.Digits;
        symbols.IsChecked = value.Symbols;
        similar.IsChecked = value.ExcludeSimilar;
    }
}
