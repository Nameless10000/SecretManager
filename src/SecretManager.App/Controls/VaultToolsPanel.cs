using SecretManager.App.ViewModels;
using SecretManager.Core.Models;
using SecretManager.Core.Services;

namespace SecretManager.App.Controls;

public sealed class VaultToolsPanel : UserControl, IDisposable
{
    private readonly MainViewModel model;
    private readonly Func<bool> isCurrent;
    private readonly StackPanel body = new() { Spacing = 12, MinWidth = 420, MaxWidth = 540 };
    private readonly ComboBox topics = new() { Header = "Ассоциация", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox title = new() { Header = "Название", MaxLength = 200 };
    private readonly ComboBox template = new() { Header = "Шаблон", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox favorite = new() { Content = "Избранное" };
    private readonly TextBox tags = new() { Header = "Теги через запятую", MaxLength = 1020 };
    private readonly CalendarDatePicker expires = new() { Header = "Срок действия (необязательно)" };
    private readonly NumberBox length = new() { Header = "Длина пароля", Minimum = 12, Maximum = 128, Value = 24 };
    private readonly CheckBox symbols = new() { Content = "Спецсимволы", IsChecked = true };
    private readonly CheckBox similar = new() { Content = "Исключить похожие символы", IsChecked = true };
    private readonly PasswordBox generated = new() { Header = "Сгенерированный пароль", PasswordRevealMode = PasswordRevealMode.Peek };
    private readonly ComboBox revisions = new() { Header = "История и корзина · последние 30 дней", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox backups = new() { Header = "Зашифрованные резервные копии", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly PasswordBox backupPassword = new() { Header = "Мастер-пароль на момент создания копии" };
    private readonly CheckBox automatic = new() { Content = "Автобэкап каждые 30 минут при открытом хранилище" };
    private readonly CheckBox confirmRestore = new() { Content = "Подтверждаю замену текущих записей выбранной копией" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock report = new() { TextWrapping = TextWrapping.Wrap };
    private bool disposed;
    public event Action<SecretField>? CopyRequested;

    public VaultToolsPanel(MainViewModel model, Func<bool> isCurrent)
    {
        this.model = model;
        this.isCurrent = isCurrent;
        Content = new ScrollViewer { Content = body, MaxHeight = 560 };
        body.Children.Add(status);
        body.Children.Add(new TextBlock { Text = "Записи, шаблоны и избранное", FontSize = 18 });
        body.Children.Add(topics);
        topics.SelectionChanged += TopicChanged;
        body.Children.Add(title);
        foreach (var name in AssociationTemplates.Names)
        {
            template.Items.Add(name);
        }

        template.SelectedIndex = 0;
        body.Children.Add(template);
        AddButton("Создать по шаблону", CreateTopic);
        body.Children.Add(favorite);
        body.Children.Add(tags);
        body.Children.Add(expires);
        AddButton("Сохранить свойства выбранной записи", SaveTopic);
        AddButton("Показать только избранное / все", ToggleFavorites);
        body.Children.Add(new TextBlock { Text = "Генератор паролей", FontSize = 18 });
        body.Children.Add(length);
        body.Children.Add(symbols);
        body.Children.Add(similar);
        body.Children.Add(generated);
        AddButton("Сгенерировать", Generate);
        AddButton("Скопировать с автоочисткой", CopyGenerated);
        body.Children.Add(revisions);
        body.Children.Add(new TextBlock { Text = "История: до 100 версий и 16 МБ, не дольше 30 дней. При переполнении создаётся зашифрованная резервная копия; крупные изменения можно вернуть из неё.", TextWrapping = TextWrapping.Wrap });
        AddButton("Восстановить выбранную версию / удалённую запись", RestoreRevision);
        body.Children.Add(automatic);
        automatic.IsChecked = model.Settings.AutomaticBackups;
        AddButton("Сохранить настройку резервирования", SaveBackupSettings);
        AddButton("Создать резервную копию сейчас", Backup);
        body.Children.Add(new TextBlock { Text = "Сохраняются последние 10 копий. При смене пароля старым копиям нужен прежний мастер-пароль.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(backups);
        body.Children.Add(backupPassword);
        body.Children.Add(confirmRestore);
        var restore = new Button { Content = "Восстановить резервную копию", HorizontalAlignment = HorizontalAlignment.Stretch };
        restore.Click += RestoreBackup;
        body.Children.Add(restore);
        AddButton("Открыть папку резервных копий", OpenBackups);
        AddButton("Проверить пароли и сроки действия", Audit);
        body.Children.Add(report);
        Refresh();
        Audit();
    }

    private void AddButton(string caption, Action action)
    {
        var button = new Button { Content = caption, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => Execute(action);
        body.Children.Add(button);
    }

    private void Execute(Action action)
    {
        if (disposed || !isCurrent())
        {
            return;
        }

        try
        {
            action();
            status.Text = "Готово";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or
            UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            status.Text = error is System.Security.Cryptography.CryptographicException ? "Неверный пароль или повреждённая копия." : error.Message;
        }
    }

    private void Refresh()
    {
        topics.ItemsSource = model.TransferDocument.Associations;
        topics.DisplayMemberPath = nameof(SecretAssociation.Title);
        revisions.ItemsSource = model.TransferDocument.Revisions.Select(item => new KeyValuePair<Guid, string>(item.Id,
            $"{item.CreatedAt.ToLocalTime():g} · {(item.Deleted ? "Корзина" : "История")} · {item.Association.Title}")).ToArray();
        revisions.DisplayMemberPath = "Value";
        backups.ItemsSource = Directory.Exists(model.BackupDirectory) ? Directory.GetFiles(model.BackupDirectory, "*.smbackup")
            .OrderByDescending(File.GetLastWriteTimeUtc).Select(path => new KeyValuePair<string, string>(path, Path.GetFileName(path))).ToArray() : [];
        backups.DisplayMemberPath = "Value";
    }

    private void TopicChanged(object sender, SelectionChangedEventArgs args)
    {
        if (topics.SelectedItem is SecretAssociation topic)
        {
            title.Text = topic.Title;
            favorite.IsChecked = topic.IsFavorite;
            tags.Text = string.Join(", ", topic.Tags);
            expires.Date = topic.ExpiresAt;
        }
    }

    private void CreateTopic()
    {
        model.SaveAssociation(AssociationTemplates.Create(title.Text.Trim(), template.SelectedIndex));
        Refresh();
    }

    private void SaveTopic()
    {
        if (topics.SelectedItem is not SecretAssociation topic)
        {
            throw new ArgumentException("Выберите ассоциацию.");
        }

        model.SaveAssociation(topic with { Title = title.Text.Trim(), IsFavorite = favorite.IsChecked == true,
            Tags = tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(), ExpiresAt = expires.Date });
        Refresh();
    }

    private void ToggleFavorites()
    {
        model.FavoritesOnly = !model.FavoritesOnly;
        model.Refresh();
    }

    private void Generate()
    {
        if (!double.IsFinite(length.Value) || length.Value != Math.Truncate(length.Value))
        {
            throw new ArgumentException("Введите целую длину пароля от 12 до 128.");
        }

        generated.Password = PasswordGenerator.Generate(checked((int)length.Value), symbols.IsChecked == true, similar.IsChecked == true);
    }

    private void CopyGenerated()
    {
        if (generated.Password.Length > 0)
        {
            CopyRequested?.Invoke(new SecretField { Description = "Сгенерированный пароль", Kind = FieldKind.Secret, Value = generated.Password });
        }
    }

    private void RestoreRevision()
    {
        if (revisions.SelectedItem is KeyValuePair<Guid, string> revision)
        {
            model.RestoreRevision(revision.Key);
            Refresh();
        }
    }

    private void SaveBackupSettings()
    {
        model.SaveSettings(model.Settings with { AutomaticBackups = automatic.IsChecked == true });
    }

    private void Backup()
    {
        model.Backup(true);
        Refresh();
    }

    private async void RestoreBackup(object sender, RoutedEventArgs args)
    {
        if (disposed || !isCurrent())
        {
            return;
        }

        try
        {
            if (confirmRestore.IsChecked != true || backups.SelectedItem is not KeyValuePair<string, string> backup)
            {
                throw new ArgumentException("Выберите копию и подтвердите замену записей.");
            }

            using var stream = File.OpenRead(backup.Key);
            var json = await SecretManager.Core.Security.ExportPackage.ReadTextAsync(stream);
            if (disposed || !isCurrent())
            {
                return;
            }

            model.RestoreBackup(json, backupPassword.Password);
            Refresh();
            status.Text = "Копия восстановлена";
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            if (!disposed && isCurrent())
            {
                status.Text = "Не удалось восстановить копию: проверьте файл и мастер-пароль.";
            }
        }
        finally
        {
            if (!disposed)
            {
                backupPassword.Password = "";
                confirmRestore.IsChecked = false;
            }
        }
    }

    private void OpenBackups()
    {
        Directory.CreateDirectory(model.BackupDirectory);
        var info = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        info.ArgumentList.Add(model.BackupDirectory);
        System.Diagnostics.Process.Start(info);
    }

    private void Audit()
    {
        var findings = VaultInsights.Audit(model.TransferDocument);
        var due = VaultInsights.Due(model.TransferDocument, DateTimeOffset.UtcNow)
            .Select(topic => $"{topic.Title}: срок {topic.ExpiresAt!.Value.ToLocalTime():d}");
        report.Text = "Локальная проверка длины и повторов, без оценки взломостойкости:\n" +
            string.Join("\n", findings.Concat(due).DefaultIfEmpty("Замечаний нет."));
    }

    public void Dispose()
    {
        disposed = true;
        generated.Password = "";
        title.Text = "";
        tags.Text = "";
        status.Text = "";
        backupPassword.Password = "";
        topics.ItemsSource = null;
        revisions.ItemsSource = null;
        report.Text = "";
        body.Children.Clear();
    }
}
