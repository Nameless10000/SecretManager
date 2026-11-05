using SecretManager.Core.Models;

namespace SecretManager.App;

public sealed partial class MainWindow
{
    private async Task CheckContextualHistory(SecretAssociation topic, SecretField original)
    {
        var current = ViewModel.TransferDocument.Associations.Single(item => item.Id == topic.Id).Fields.Single(item => item.Id == original.Id);
        var cancelled = Run(() => EditField(topic.Id, current));
        await Wait(() => activeDialog?.IsLoaded == true, "field history cancellation");
        await ExpandEditorHistory();
        var history = Elements(activeDialog!).OfType<ComboBox>().Single(item => item.Header as string == "История этого поля");
        history.SelectedItem = history.Items.Cast<ComboBoxItem>().First(item => item.Tag is SecretField field && field.Value == original.Value);
        InvokeProductivityButton(activeDialog!, "Подставить версию поля");
        await Wait(() => Elements(activeDialog!).OfType<PasswordBox>().Single().Password == original.Value, "load previous field value");
        activeDialog!.Hide();
        await cancelled;
        Check(ViewModel.TransferDocument.Associations.Single(item => item.Id == topic.Id).Fields.Single(item => item.Id == original.Id) == current,
            "Cancelling a history preview changed the stored field.");

        ViewModel.DeleteField(topic.Id, current.Id);
        var restoring = Run(() => EditField(topic.Id, null));
        await Wait(() => activeDialog?.IsLoaded == true, "deleted field editor");
        await ExpandEditorHistory();
        history = Elements(activeDialog!).OfType<ComboBox>().Single(item => item.Header as string == "Удалённые поля этой ассоциации");
        history.SelectedItem = history.Items.Cast<ComboBoxItem>().First(item => item.Tag is SecretField field && field.Id == current.Id && field.Value == current.Value);
        InvokeProductivityButton(activeDialog!, "Подставить версию поля");
        await Wait(() => Elements(activeDialog!).OfType<PasswordBox>().Single().Password == current.Value, "load deleted field value");
        InvokeProductivityButton(activeDialog!, "Сохранить");
        await restoring;
        Check(ViewModel.TransferDocument.Associations.Single(item => item.Id == topic.Id).Fields.Single(item => item.Id == current.Id) == current,
            "Restoring a deleted field lost its ID, value or expiry.");

        ViewModel.DeleteAssociation(topic.Id);
        var restoreAssociation = Run(() => EditAssociation(null));
        await Wait(() => activeDialog?.IsLoaded == true, "deleted association editor");
        await ExpandEditorHistory();
        var versions = Elements(activeDialog!).OfType<ComboBox>().Single(item => item.Header as string == "Версия");
        versions.SelectedItem = versions.Items.Cast<ComboBoxItem>().First(item => item.Tag is SecretAssociation association && association.Id == topic.Id);
        InvokeProductivityButton(activeDialog!, "Подставить выбранную версию");
        InvokeProductivityButton(activeDialog!, "Сохранить");
        await restoreAssociation;
        var restored = ViewModel.TransferDocument.Associations.Single(item => item.Id == topic.Id);
        Check(restored.IsFavorite && restored.Tags.Count == 2 && restored.Fields.Single(item => item.Id == current.Id) == current,
            "Restoring a deleted association lost its metadata or fields.");
    }

    private async Task ExpandEditorHistory()
    {
        Elements(activeDialog!).OfType<Expander>().Single().IsExpanded = true;
        Root.UpdateLayout();
        await Task.Delay(150);
    }

    private async Task CheckSettingsNavigation()
    {
        SettingsPanel.Visibility = Visibility.Visible;
        var categories = SettingsSections.Children.OfType<Expander>().ToArray();
        Check(categories.Length == 8 && categories.All(category => !category.IsExpanded), "Settings did not start as collapsed categories.");
        foreach (var category in categories)
        {
            category.IsExpanded = true;
            Root.UpdateLayout();
            await Task.Delay(80);
            Check(categories.Count(item => item.IsExpanded) == 1, "Settings category did not collapse the previous section.");
        }

        categories.Single(category => category.Header as string == "Горячие клавиши").IsExpanded = true;
        Root.UpdateLayout();
        await Task.Delay(100);
        ShortcutSettings.Load(new Services.HotkeyGesture(0x87, 6));
        SaveShortcut_Click(this, new RoutedEventArgs());
        Check(Services.DevicePreferences.Load(Services.AppPaths.DataDirectory).QuickAccess == new Services.HotkeyGesture(0x87, 6),
            "Edited shortcut did not persist.");
        await SaveUsageSnapshot(SettingsPanel, Path.Combine(Services.AppPaths.DataDirectory, "settings-shortcut.png"));
        foreach (var category in categories)
        {
            category.IsExpanded = false;
        }

        await SaveUsageSnapshot(SettingsPanel, Path.Combine(Services.AppPaths.DataDirectory, "settings-categories.png"));
        SettingsPanel.Visibility = Visibility.Collapsed;
    }
}
