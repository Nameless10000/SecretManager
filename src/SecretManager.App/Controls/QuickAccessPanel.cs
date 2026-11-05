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
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(search);
        panel.Children.Add(new ScrollViewer { Content = results, MaxHeight = 380 });
        Content = panel;
        search.TextChanged += SearchChanged;
        Refresh();
    }

    private void SearchChanged(object sender, TextChangedEventArgs args)
    {
        Refresh();
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
        search.Text = "";
        results.Children.Clear();
    }
}
