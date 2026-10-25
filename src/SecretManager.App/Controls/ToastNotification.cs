using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Data;

namespace SecretManager.App.Controls;

public sealed class ToastNotification : UserControl
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 340, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly FontIcon icon = new() { Glyph = "\uE73E", FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    public bool IsOpen => Visibility == Visibility.Visible;
    public string Message => message.Text;

    public ToastNotification()
    {
        Visibility = Visibility.Collapsed;
        var layout = new Grid { ColumnSpacing = 10 };
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(message, 1);
        var close = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
            Padding = new Thickness(4), MinWidth = 0, MinHeight = 0, Width = 24, Height = 24,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Закрыть уведомление");
        Grid.SetColumn(close, 2); close.Click += (_, _) => Hide();
        layout.Children.Add(icon); layout.Children.Add(message); layout.Children.Add(close);
        var shell = new Border { Padding = new Thickness(12, 9, 8, 9), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = layout };
        shell.SetBinding(Border.BackgroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Background)) });
        shell.SetBinding(Border.BorderBrushProperty, new Binding { Source = this, Path = new PropertyPath(nameof(BorderBrush)) });
        Content = shell;
        timer.Tick += (_, _) => Hide();
        Unloaded += (_, _) => timer.Stop();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(this, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
    }
    public void Show(string text, bool error)
    {
        timer.Stop(); message.Text = text;
        icon.Glyph = error ? "\uE7BA" : "\uE73E";
        Visibility = Visibility.Visible;
        timer.Start();
    }
    public void Hide() { timer.Stop(); Visibility = Visibility.Collapsed; message.Text = ""; }
}
