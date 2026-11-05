using SecretManager.App.ViewModels;

namespace SecretManager.App.Controls;

public sealed class VaultBackupPanel : UserControl, IDisposable
{
    private readonly MainViewModel model;
    private readonly Func<bool> isCurrent;
    private readonly ComboBox backups = new() { Header = "Резервная копия", DisplayMemberPath = "Value", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly PasswordBox backupPassword = new() { Header = "Мастер-пароль на момент создания копии" };
    private readonly CheckBox confirmRestore = new() { Content = "Подтверждаю замену текущих записей выбранной копией" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel body = new() { Spacing = 12, MinWidth = 380 };
    private readonly Button restore = new() { Content = "Восстановить резервную копию" };
    private bool disposed;
    private bool restoring;

    public VaultBackupPanel(MainViewModel model, Func<bool> isCurrent)
    {
        this.model = model;
        this.isCurrent = isCurrent;
        body.Children.Add(new TextBlock { Text = "Восстановление заменит записи хранилища. Перед ним будет создана копия текущего состояния.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(backups);
        body.Children.Add(backupPassword);
        body.Children.Add(confirmRestore);
        restore.Click += RestoreBackup;
        body.Children.Add(restore);
        body.Children.Add(status);
        Content = body;
        Refresh();
    }

    private void Refresh()
    {
        backups.ItemsSource = Directory.Exists(model.BackupDirectory) ? Directory.GetFiles(model.BackupDirectory, "*.smbackup")
            .OrderByDescending(File.GetLastWriteTimeUtc).Select(path => new KeyValuePair<string, string>(path, File.GetLastWriteTime(path).ToString("g"))).ToArray() : [];
    }

    private async void RestoreBackup(object sender, RoutedEventArgs args)
    {
        if (disposed || restoring || !isCurrent())
        {
            return;
        }

        restoring = true;
        restore.IsEnabled = false;
        var password = backupPassword.Password;
        backupPassword.Password = "";
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

            model.RestoreBackup(json, password);
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
            password = "";
            restoring = false;
            if (!disposed)
            {
                restore.IsEnabled = true;
                backupPassword.Password = "";
                confirmRestore.IsChecked = false;
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        backupPassword.Password = "";
        backups.ItemsSource = null;
        body.Children.Clear();
    }
}
