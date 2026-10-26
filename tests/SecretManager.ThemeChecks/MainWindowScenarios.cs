using Microsoft.UI.Xaml.Media;
using SecretManager.App.Services;
using SecretManager.Core.Models;
using SecretManager.Core.Services;

namespace SecretManager.App;

// Compiled only into the regression host alongside the production MainWindow.
// No test hooks, alternative login path or test passwords are shipped in the app.
public sealed partial class MainWindow
{
    internal async Task<int> RunUsageScenarios(string output, Action<string> stage)
    {
        const string password = "synthetic UI test password 123";
        var count = 0;
        void Pass(string name) { stage(name); count++; }
        await Wait(() => Root.IsLoaded, "main window load");
        Check(!ViewModel.IsUnlocked && LoginView.Visibility == Visibility.Visible, "Startup did not show the login form.");
        Pass("startup locked");

        async Task Login(string value, bool expected)
        {
            LoginPassword.Password = value;
            LoginConfirmation.Password = ViewModel.HasVault ? "" : value;
            Unlock_Click(this, new RoutedEventArgs());
            await Wait(() => !actionBusy && !ViewModel.IsBusy, "login completion");
            Check(ViewModel.IsUnlocked == expected, "Unexpected login result.");
            Check(LoginPassword.Password.Length == 0 && LoginConfirmation.Password.Length == 0, "Password controls retained input.");
        }
        await Login("short", false); Check(LoginError.IsOpen, "Weak password did not show validation."); Pass("weak password rejected");
        await Login(password, true); Check(VaultView.Visibility == Visibility.Visible, "Vault UI stayed hidden."); Pass("create vault through login form");

        var first = new SecretAssociation { Title = "Почта 1" };
        var second = new SecretAssociation { Title = "Mail" };
        ViewModel.SaveAssociation(first); ViewModel.SaveAssociation(second); ViewModel.SaveAssociation(new() { Title = "Другой сайт" });
        var login = new SecretField { Description = "Логин", Value = "sample@example.invalid" };
        ViewModel.SaveField(first.Id, login);
        ViewModel.SaveField(first.Id, new() { Description = "Пароль", Kind = FieldKind.Secret, Value = "synthetic field value" });
        ViewModel.SaveField(first.Id, new() { Description = "Файл", Kind = FieldKind.File, FileName = "key.bin", Value = Convert.ToBase64String([0, 255, 1]) });
        ViewModel.SaveField(first.Id, new() { Description = "Картинка", Kind = FieldKind.Image, FileName = "sample.png", ContentType = "image/png",
            Value = Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(output, "attachment-fixtures", "sample.png"))) });
        Root.UpdateLayout(); Pass("association and all four field kinds");

        stage("select individual transfer fields");
        var transfer = AskSelection(ViewModel.TransferDocument, "Synthetic selection check");
        await Wait(() => activeDialog?.IsLoaded == true, "transfer selector load");
        var selection = (SecretManager.App.Controls.TransferSelection)activeDialog!.Content;
        Check(selection.Capture().Count == 3 && selection.Capture().Values.Sum(f => f.Count) == 4, "Default selection omitted fields.");
        selection.ClearSelection();
        await Wait(() => !activeDialog.IsPrimaryButtonEnabled, "empty selection validation");
        Check(!activeDialog.IsPrimaryButtonEnabled, "Empty transfer was allowed.");
        var selectedTopic = selection.Tree.RootNodes.Single(n => n.Children.Count > 0);
        selectedTopic.IsExpanded = true;
        selection.Tree.SelectedNodes.Add(selectedTopic.Children[0]); selection.UpdateSummary(); Root.UpdateLayout();
        await Wait(() => activeDialog.IsPrimaryButtonEnabled, "selected field validation");
        Check(selection.Capture().Count == 1 && selection.Capture().Values.Single().Count == 1, "Individual field selection failed.");
        Check(activeDialog.IsPrimaryButtonEnabled, "Selected field could not be transferred.");
        await SaveUsageSnapshot(activeDialog, Path.Combine(output, "transfer-selection.png"));
        activeDialog.Hide(); Check(await transfer is null, "Cancelled selection was accepted.");
        Pass("transfer defaults, empty guard, individual field and cancel");

        var lockTransfer = Run(async () => { await AskSelection(ViewModel.TransferDocument, "Synthetic lock selection check"); });
        await Wait(() => activeDialog?.IsLoaded == true, "selector before lock");
        ViewModel.Lock(); await lockTransfer; CheckLocked(); await Login(password, true);
        Pass("lock cancels transfer selection");

        SearchBox.Text = "ПОЧТА Mail";
        await Wait(() => ViewModel.Query == "ПОЧТА Mail" && ViewModel.Associations.Count == 2, "search binding");
        Check(ViewModel.TotalCount == 3, "Search changed the total count."); Pass("OR search through actual search control");
        All_Click(this, new RoutedEventArgs()); Check(ViewModel.Associations.Count == 3, "Clear search did not restore the list.");
        ViewModel.Associations[0].IsExpanded = true; ViewModel.Associations[1].IsExpanded = true;
        Root.UpdateLayout();
        Check(ViewModel.Associations.Count(a => a.IsExpanded) == 1, "Accordion did not close the previous item."); Pass("accordion after search refresh");

        foreach (var (theme, index) in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.Midnight, AppTheme.Forest, AppTheme.Custom, AppTheme.System }.Select((t, i) => (t, i)))
        {
            stage("settings preview " + theme);
            Settings_Click(this, new RoutedEventArgs());
            ThemeChoice.SelectedIndex = (int)theme;
            AnimationChoice.SelectedIndex = index % 3; MotionSpeedChoice.Value = index % 2 == 0 ? .5 : 2;
            CompactChoice.IsOn = index % 2 == 0; SingleExpandedChoice.IsOn = index % 2 != 0;
            FontChoice.SelectedItem = index % 2 == 0 ? "Consolas" : "Segoe UI Variable";
            Root.UpdateLayout(); await Task.Delay(250);
            CheckAllToggles();
            SaveSettings_Click(this, new RoutedEventArgs()); await Wait(() => !actionBusy, "save settings");
            Settings_Click(this, new RoutedEventArgs()); Settings_Click(this, new RoutedEventArgs());
            Root.UpdateLayout(); await Task.Delay(250);
            CheckAllToggles();
            Check(CompactChoice.IsOn == ViewModel.Settings.CompactLayout && SingleExpandedChoice.IsOn == ViewModel.Settings.SingleExpandedAssociation,
                "Reopened settings disagree with saved values.");
            Settings_Click(this, new RoutedEventArgs()); Pass("settings save and reopen " + theme);
        }

        foreach (var size in new[] { 10d, 26d })
        {
            Settings_Click(this, new RoutedEventArgs()); FontChoice.SelectedItem = "Comic Sans MS"; FontSizeChoice.Value = size;
            Root.UpdateLayout(); Check(appearancePreview?.FontSize == size, "Font slider did not preview the boundary.");
            var settingsHeading = Elements(SettingsPanel).OfType<TextBlock>().First(t => t.Text == "Настройки");
            Check(Math.Abs(settingsHeading.FontSize - 23 * size / 14) < .01, $"Settings heading font size drift: {settingsHeading.FontSize} at {size}.");
            await SaveUsageSnapshot(Root, Path.Combine(output, $"font-{size:0}.png"));
            SaveSettings_Click(this, new RoutedEventArgs()); await Wait(() => !actionBusy, "font settings save");
            Settings_Click(this, new RoutedEventArgs()); Settings_Click(this, new RoutedEventArgs());
            Check(FontSizeChoice.Value == size && FontChoice.SelectedItem as string == "Comic Sans MS", "Font settings failed to reopen.");
            Settings_Click(this, new RoutedEventArgs());
        }
        ViewModel.SaveSettings(ViewModel.Settings with { FontSize = 14, FontFamily = "Segoe UI Variable" }); ApplyAppearance();
        Pass("Comic Sans and 10–26 slider save and reopen");

        Navigation.IsPaneOpen = true; Root.UpdateLayout(); await Task.Delay(350);
        var pane = (Grid)Navigation.Pane;
        var buttons = pane.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<Button>()).ToArray();
        Check(buttons.All(b => b.HorizontalContentAlignment == HorizontalAlignment.Left), "Sidebar buttons are not aligned left.");
        var top = QuickNavigation.TransformToVisual(pane).TransformPoint(new Windows.Foundation.Point()).Y;
        var bottom = ((FrameworkElement)pane.Children[2]).TransformToVisual(pane).TransformPoint(new Windows.Foundation.Point()).Y;
        Check(bottom - (top + QuickNavigation.ActualHeight) <= 14 && QuickNavigation.Margin.Top == 10, "Quick access does not fill the space above the actions.");
        Check(SidebarExitButton.Command is null, "Sidebar exit still invokes a lock command.");
        await SaveUsageSnapshot(Root, Path.Combine(output, "sidebar.png"));
        Pass("left aligned sidebar and full height quick access");

        var syntheticRegistryPath = @"Software\SecretManager\SyntheticTests\" + Guid.NewGuid().ToString("N");
        try
        {
            var startup = new StartupRegistration(Path.Combine(output, "synthetic app.exe"), syntheticRegistryPath + @"\Run");
            Check(!startup.Enabled, "Synthetic startup initially enabled.");
            startup.SetEnabled(true); Check(startup.Enabled && startup.Command.EndsWith("\" --startup"), "Startup command did not round trip.");
            startup.SetEnabled(false); Check(!startup.Enabled, "Startup registration did not clear.");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(syntheticRegistryPath, false); }
        Pass("startup registry enable and disable in isolated synthetic key");

        StartWindowsServices(true); Root.UpdateLayout();
        Check(!ViewModel.IsUnlocked && !AppWindow.IsVisible, "Windows startup did not hide a locked vault in the tray.");
        ShowWindow(); await Login(password, true);
        Pass("startup in tray remains locked and can be reopened");

        stage("lock while editing a field");
        var edit = Run(() => EditField(first.Id, null));
        await Wait(() => activeDialog?.IsLoaded == true, "field editor");
        ViewModel.LockCommand.Execute(null); await edit;
        CheckLocked(); await Login(password, true);
        Check(ViewModel.Associations.Single(a => a.Model.Id == first.Id).Fields.Count == 4, "Cancelled editor changed fields.");
        Pass("lock cancels open field editor");

        ViewModel.Lock(); await Login("wrong synthetic password", false);
        Check(LoginError.IsOpen && ViewModel.Associations.Count == 0, "Wrong password revealed records.");
        await Login(password, true); Pass("bad password then successful re-entry");

        stage("encrypted export and merge");
        var backup = await ViewModel.ExportAsync("synthetic export password 123");
        ViewModel.SaveField(first.Id, login with { Value = "edited@example.invalid" });
        var imported = VaultSession.ReadExport(backup, "synthetic export password 123");
        var conflicts = ViewModel.FindConflicts(imported);
        Check(conflicts.Count == 1, "Expected a merge conflict.");
        ViewModel.Import(imported, conflicts.ToDictionary(c => c.Imported.Id, _ => ConflictResolution.UseImported));
        Check(ViewModel.Associations.Single(a => a.Model.Id == first.Id).Fields.Single(f => f.Model.Id == login.Id).Model.Value == login.Value,
            "Import did not restore the field."); Pass("encrypted export and conflict import with live bindings");
        await Run(() => { _ = VaultSession.ReadExport(backup, "wrong export password"); return Task.CompletedTask; });
        Check(ViewModel.IsUnlocked && Notice.IsOpen && ViewModel.TotalCount == 3, "Rejected import damaged current UI.");
        Pass("wrong export password leaves open vault intact");

        stage("lock while image preview is open");
        var image = ViewModel.Associations.Single(a => a.Model.Id == first.Id).Fields.Single(f => f.IsImage);
        var preview = Run(() => Preview(image));
        await Wait(() => activeDialog?.IsLoaded == true, "image preview");
        ViewModel.Lock(); await preview; CheckLocked(); await Login(password, true);
        Pass("lock cancels image preview");

        for (var i = 0; i < 3; i++)
        {
            var button = Elements(Root).OfType<Button>().First(b => ReferenceEquals(b.Command, ViewModel.LockCommand));
            button.Command.Execute(null); CheckLocked(); await Login(password, true);
            Check(ViewModel.TotalCount == 3, "Repeated lock/re-entry lost records.");
        }
        Pass("repeated bound lock command and re-entry");

        stage("restore after focus timeout");
        AppWindow.Hide(); await Task.Delay(50);
        var now = DateTimeOffset.UtcNow;
        focus.SetFocused(true, now); focus.SetFocused(false, now.AddMinutes(-6));
        ShowWindow(); CheckLocked(); await Login(password, true);
        Pass("window restore enforces elapsed focus timeout");

        await ViewModel.ChangePasswordAsync(password, "changed synthetic UI password 123");
        ViewModel.Lock(); await Login(password, false); await Login("changed synthetic UI password 123", true);
        Check(ViewModel.TotalCount == 3, "Password change lost records."); Pass("password change and old-password rejection");
        ViewModel.DeleteField(first.Id, login.Id); ViewModel.DeleteAssociation(second.Id);
        Check(ViewModel.TotalCount == 2 && ViewModel.Associations.Single(a => a.Model.Id == first.Id).Fields.Count == 3,
            "Deleting a field/association left stale bindings."); Pass("delete field and association with live bindings");
        ViewModel.Lock(); await Login("changed synthetic UI password 123", true);
        Check(ViewModel.TotalCount == 2 && ViewModel.Associations.Single(a => a.Model.Id == first.Id).Fields.Count == 3,
            "Deletes were not preserved after re-entry."); Pass("re-entry preserves deletions");
        return count;
    }

    internal async Task CloseAfterUsageChecks()
    {
        var closed = new TaskCompletionSource();
        Closed += (_, _) => closed.TrySetResult();
        var release = new TaskCompletionSource();
        async Task CompleteLater() { await release.Task; Notify("Синтетическое позднее завершение"); }
        var pending = CompleteLater();
        ViewModel.Lock(); LoginPassword.Password = "changed synthetic UI password 123";
        Unlock_Click(this, new RoutedEventArgs());
        Exit_Click(SidebarExitButton, new RoutedEventArgs());
        await Wait(() => closed.Task.IsCompleted, "window exit");
        release.TrySetResult(); await pending;
    }
    private void CheckAllToggles()
    {
        foreach (var toggle in new[] { CompactChoice, SingleExpandedChoice, TrayChoice, LockHiddenChoice, StartupChoice, AutoUpdateChoice })
            SecretManager.ThemeChecks.ThemeCheckWindow.CheckToggle(toggle);
    }
    private void CheckLocked() => Check(!ViewModel.IsUnlocked && ViewModel.Associations.Count == 0 && LoginView.Visibility == Visibility.Visible &&
        VaultView.Visibility == Visibility.Collapsed && SettingsPanel.Visibility == Visibility.Collapsed, "Lock did not clear and hide the vault UI.");
    private static void Check(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static async Task SaveUsageSnapshot(FrameworkElement element, string path)
    {
        await Task.Delay(400); // Let native expansion and text rendering reach their final frame.
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var buffer = await bitmap.GetPixelsAsync(); var bytes = new byte[buffer.Length];
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        using var file = File.Create(path); using var stream = file.AsRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private static async Task Wait(Func<bool> ready, string operation)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!ready()) { if (DateTime.UtcNow >= until) throw new TimeoutException("Usage scenario timeout: " + operation); await Task.Delay(20); }
    }
    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Elements(child)) yield return next;
        }
    }
}
