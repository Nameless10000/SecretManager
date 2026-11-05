using SecretManager.App.ViewModels;
using SecretManager.Core.Models;

namespace SecretManager.App.Controls;

public sealed class QuickAccessPanel : UserControl, IDisposable
{
    private readonly MainViewModel model;
    private bool disposed;
    private readonly Func<bool> isCurrent;
    private readonly TextBox search = new() { PlaceholderText = "Название, тег или описание поля", MinWidth = 380 };
    private readonly StackPanel results = new() { Spacing = 8 };
    public event Action<SecretField>? CopyRequested;

    public QuickAccessPanel(MainViewModel model, Func<bool> isCurrent)
    {
        this.model = model;
        this.isCurrent = isCurrent;
        var panel = new Grid { RowSpacing = 12 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(search);
        var scroll = new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);
        Content = panel;
        Loaded += (_, _) => FocusSearch();
        search.TextChanged += SearchChanged;
        search.KeyDown += SearchKeyDown;
        model.PropertyChanged += ModelChanged;
        Refresh();
    }

    private void SearchChanged(object sender, TextChangedEventArgs args)
    {
        Refresh();
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.CountLabel))
        {
            Refresh();
        }
    }

    private void SearchKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Enter && results.Children.OfType<Button>().FirstOrDefault() is { } first)
        {
            CopyClicked(first, new RoutedEventArgs());
            args.Handled = true;
        }
    }

    public void FocusSearch()
    {
        search.Focus(FocusState.Programmatic);
    }

    private void Refresh()
    {
        results.Children.Clear();
        if (disposed || !isCurrent())
        {
            return;
        }

        foreach (var topic in model.TransferDocument.Associations.OrderByDescending(item => item.IsFavorite).ThenBy(item => item.Title))
        {
            foreach (var field in topic.Fields)
            {
                var label = $"{topic.Title} / {field.Description}";
                if (search.Text.Length > 0 && !(label + " " + string.Join(" ", topic.Tags)).Contains(search.Text, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                var button = new Button { Content = label, Tag = field, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += CopyClicked;
                results.Children.Add(button);
            }
        }

        if (results.Children.Count == 0)
        {
            results.Children.Add(new TextBlock { Text = "Ничего не найдено", TextWrapping = TextWrapping.Wrap });
        }
    }

    private void CopyClicked(object sender, RoutedEventArgs args)
    {
        if (!disposed && isCurrent() && sender is Button { Tag: SecretField field })
        {
            CopyRequested?.Invoke(field);
        }
    }

    public void Dispose()
    {
        disposed = true;
        search.TextChanged -= SearchChanged;
        search.KeyDown -= SearchKeyDown;
        model.PropertyChanged -= ModelChanged;
        search.Text = "";
        results.Children.Clear();
    }
}
