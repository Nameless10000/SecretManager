using SecretManager.Core.Models;
using SecretManager.Core.Services;

namespace SecretManager.App.Controls;

public sealed class AssociationEditor : UserControl, IDisposable
{
    private SecretAssociation draft;
    private readonly bool creating;
    private readonly TextBox title = new() { Header = "Название", MaxLength = 200 };
    private readonly TextBox tags = new() { Header = "Теги через запятую", MaxLength = 1020 };
    private readonly CheckBox favorite = new() { Content = "Избранное" };
    private readonly ComboBox template = new() { Header = "Шаблон", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox versions = new() { Header = "Версия", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel body = new() { Spacing = 12, MinWidth = 380 };
    public TextBlock Error { get; } = new() { TextWrapping = TextWrapping.Wrap };

    public AssociationEditor(SecretAssociation? existing, VaultDocument document)
    {
        creating = existing is null;
        draft = existing ?? new SecretAssociation();
        Content = new ScrollViewer { Content = body, MaxHeight = 520 };
        body.Children.Add(title);
        if (creating)
        {
            foreach (var name in AssociationTemplates.Names)
            {
                template.Items.Add(name);
            }

            template.SelectedIndex = 0;
            template.SelectionChanged += (_, _) => UpdateTemplateSummary();
            body.Children.Add(template);
        }

        body.Children.Add(favorite);
        body.Children.Add(tags);

        body.Children.Add(summary);
        var relevant = document.Revisions.Where(item => creating
            ? item.Deleted && !document.Associations.Any(topic => topic.Id == item.Association.Id)
            : item.Association.Id == draft.Id).OrderByDescending(item => item.CreatedAt);
        versions.ItemsSource = relevant.Select(item => new ComboBoxItem { Tag = item.Association,
            Content = $"{item.CreatedAt.ToLocalTime():g} · {item.Association.Title} · {item.Association.Fields.Count} полей" }).ToArray();
        var history = new StackPanel { Spacing = 10 };
        history.Children.Add(versions);
        history.Children.Add(new TextBlock { Text = "Выбранная версия целиком подставится в редактор, включая поля. Изменения применятся после сохранения.", TextWrapping = TextWrapping.Wrap });
        var restore = new Button { Content = "Подставить выбранную версию" };
        restore.Click += LoadVersion;
        history.Children.Add(restore);
        body.Children.Add(new Expander { Header = creating ? "Восстановить удалённую ассоциацию" : "История изменений", Content = history,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(Error);
        LoadDraft();
    }

    private void UpdateTemplateSummary()
    {
        if (template.IsEnabled)
        {
            var fields = AssociationTemplates.Create("", template.SelectedIndex).Fields;
            summary.Text = fields.Count == 0 ? "Пустая ассоциация — поля можно добавить после создания." :
                "Будут добавлены поля: " + string.Join(", ", fields.Select(field => field.Description));
        }
    }

    private void LoadVersion(object sender, RoutedEventArgs args)
    {
        if (versions.SelectedItem is ComboBoxItem { Tag: SecretAssociation selected })
        {
            draft = selected;
            template.IsEnabled = false;
            LoadDraft();
        }
    }

    private void LoadDraft()
    {
        title.Text = draft.Title;
        tags.Text = string.Join(", ", draft.Tags);
        favorite.IsChecked = draft.IsFavorite;

        summary.Text = $"Полей: {draft.Fields.Count}";
        if (creating && template.IsEnabled)
        {
            UpdateTemplateSummary();
        }
    }

    public SecretAssociation BuildValue()
    {
        var value = creating && template.IsEnabled ? AssociationTemplates.Create(title.Text.Trim(), template.SelectedIndex) : draft;
        value = value with
        {
            Title = title.Text.Trim(), IsFavorite = favorite.IsChecked == true,
            Tags = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(), UpdatedAt = DateTimeOffset.UtcNow
        };
        VaultValidation.Validate(new VaultDocument { Associations = [value] });
        return value;
    }

    public void Dispose()
    {
        title.Text = "";
        tags.Text = "";
        versions.ItemsSource = null;
        draft = new SecretAssociation();
        body.Children.Clear();
    }
}
