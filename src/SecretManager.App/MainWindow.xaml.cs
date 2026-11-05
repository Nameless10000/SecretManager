using System.ComponentModel;
using System.Security.Cryptography;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using SecretManager.App.Services;
using SecretManager.App.Controls;
using SecretManager.App.ViewModels;
using SecretManager.Core.Models;
using SecretManager.Core.Security;
using SecretManager.Core.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace SecretManager.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private GlobalHotkey? globalHotkey;
    private DateTimeOffset nextMaintenance;
    private readonly ClipboardService clipboard;
    private readonly MotionService motion;
    private readonly TrayService tray;
    private readonly FocusLockPolicy focus = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly nint hwnd;
    private ContentDialog? activeDialog;
    private bool exiting;
    private bool actionBusy;
    private bool loadingSettings = true;
    private bool isFocused = true;
    private bool navigationWasOpen;
    private bool applyingAppearance;
    private int epoch;
    private string accent = "#5978FF";
    private string background = "#171B27";
    private AppSettings? appearancePreview;

    public MainWindow()
    {
        var dataFolder = AppPaths.DataDirectory;
        ViewModel = new MainViewModel(Path.Combine(dataFolder, "vault.db"));
        InitializeComponent();
        updateNoticeTimer.Tick += DismissUpdateNotice;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true; SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1280, 850));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.PreferredMinimumWidth = 980;
        clipboard = new ClipboardService();
        motion = new MotionService(() => appearancePreview ?? ViewModel.Settings);
        tray = new TrayService(ShowWindow, () => { if (!exiting) ViewModel.Lock(); }, ExitApplication);
        ViewModel.SessionChanged += UpdateSession;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Activated += Window_Activated;
        AppWindow.Closing += AppWindow_Closing;
        Closed += Window_Closed;
        Root.Loaded += (_, _) => { UpdateSession(); ApplyAppearance(); motion.Wire(Root); motion.Enter(LoginView); LoginPassword.Focus(FocusState.Programmatic); };
        Root.ActualThemeChanged += (_, _) =>
        {
            if (!exiting && !applyingAppearance && (appearancePreview ?? ViewModel.Settings).Theme == AppTheme.System) ApplyAppearance();
        };
        timer.Tick += (_, _) => { CheckFocusTimeout(); TryApplyAutomaticUpdate(); }; timer.Start();
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        var lockKey = new KeyboardAccelerator { Key = VirtualKey.L, Modifiers = VirtualKeyModifiers.Control };
        lockKey.Invoked += (_, e) => { if (!exiting) ViewModel.Lock(); e.Handled = true; };
        Root.KeyboardAccelerators.Add(lockKey);
        var findKey = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        findKey.Invoked += (_, e) =>
        {
            if (ViewModel.IsUnlocked && quickAccessWindow is null)
            {
                SearchBox.Focus(FocusState.Programmatic);
            }

            e.Handled = true;
        };
        Root.KeyboardAccelerators.Add(findKey);
        ShortcutSettings.Load(DevicePreferences.Load(AppPaths.DataDirectory).QuickAccess);
        ConfigureLogin();
        if (Environment.GetEnvironmentVariable("SECRETMANAGER_DATA_DIR") is null)
        {
            var shortcut = DevicePreferences.Load(AppPaths.DataDirectory).QuickAccess;
            if (shortcut.Enabled)
            {
                globalHotkey = new GlobalHotkey(shortcut.Key, shortcut.Modifiers);
                if (!globalHotkey.Registered)
                {
                    ShortcutStatus.Text = "Сочетание занято. Выберите другое и нажмите «Применить сочетание».";
                }
            }
            if (globalHotkey is not null)
            {
                globalHotkey.Invoked += GlobalQuickAccess;
            }
        }

        timer.Tick += MaintenanceTick;
    }

    private async void CopyQuickField(SecretField field)
    {
        var window = quickAccessWindow;
        try
        {
            await CopyField(new FieldViewModel(field));
            if (window is not null && ReferenceEquals(window, quickAccessWindow))
            {
                window.Close();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            if (!exiting && ViewModel.IsUnlocked)
            {
                Notify("Не удалось скопировать значение. Повторите попытку.", true);
            }
        }
    }

    private QuickAccessWindow? quickAccessWindow;

    private void GlobalQuickAccess()
    {
        if (exiting)
        {
            return;
        }

        CheckFocusTimeout();
        if (quickAccessWindow is null)
        {
            quickAccessWindow = new QuickAccessWindow(ViewModel);
            quickAccessWindow.CopyRequested += CopyQuickField;
            quickAccessWindow.FocusChanged += QuickAccessFocusChanged;
            quickAccessWindow.Closed += QuickAccessClosed;
        }

        quickAccessWindow.Activate();
        quickAccessWindow.FocusInput();
    }

    private void QuickAccessClosed(object sender, WindowEventArgs args)
    {
        quickAccessWindow = null;
    }

    private void QuickAccessFocusChanged(bool focused)
    {
        focus.SetFocused(isFocused || focused, DateTimeOffset.UtcNow);
    }

    private void SettingsCategory_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        foreach (var category in SettingsSections.Children.OfType<Expander>())
        {
            if (!ReferenceEquals(category, sender))
            {
                category.IsExpanded = false;
            }
        }
    }

    private void Favorites_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.IsUnlocked)
        {
            ViewModel.FavoritesOnly = !ViewModel.FavoritesOnly;
            ViewModel.Refresh();
        }
    }

    private async void GeneratorSettings_Click(object sender, RoutedEventArgs args)
    {
        await Run(async () =>
        {
            var captured = epoch;
            RequireSession(captured);
            var editor = new PasswordGeneratorSettingsControl();
            editor.Load(ViewModel.Settings.PasswordGenerator);
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(editor);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            body.Children.Add(error);
            var dialog = Dialog("Параметры генератора", body, "Сохранить");
            dialog.PrimaryButtonClick += (_, click) =>
            {
                try
                {
                    RequireSession(captured);
                    ViewModel.SaveSettings(ViewModel.Settings with { PasswordGenerator = editor.Capture() });
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or OperationCanceledException)
                {
                    error.Text = exception.Message;
                    click.Cancel = true;
                }
            };
            await ShowDialog(dialog);
        });
    }

    private void SaveShortcut_Click(object sender, RoutedEventArgs args)
    {
        GlobalHotkey? candidate = null;
        try
        {
            var gesture = ShortcutSettings.Capture();
            var current = DevicePreferences.Load(AppPaths.DataDirectory);
            var replace = gesture != current.QuickAccess || gesture.Enabled && globalHotkey?.Registered != true;
            if (replace && gesture.Enabled && Environment.GetEnvironmentVariable("SECRETMANAGER_DATA_DIR") is null)
            {
                candidate = new GlobalHotkey(gesture.Key, gesture.Modifiers);
                if (!candidate.Registered)
                {
                    throw new ArgumentException("Сочетание занято другой программой или зарезервировано Windows. Выберите другое.");
                }
            }

            (current with { QuickAccess = gesture }).Save(AppPaths.DataDirectory);
            if (replace)
            {
                globalHotkey?.Dispose();
                globalHotkey = candidate;
                candidate = null;
                if (globalHotkey is not null)
                {
                    globalHotkey.Invoked += GlobalQuickAccess;
                }
            }

            ShortcutStatus.Text = "Сочетание сохранено";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ShortcutStatus.Text = exception.Message;
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    private void MaintenanceTick(object? sender, object args)
    {
        if (exiting || !ViewModel.IsUnlocked || DateTimeOffset.UtcNow < nextMaintenance)
        {
            return;
        }

        nextMaintenance = DateTimeOffset.UtcNow.AddMinutes(30);
        try
        {
            ViewModel.Backup();
            var due = SecretManager.Core.Services.VaultInsights.Due(ViewModel.TransferDocument, DateTimeOffset.UtcNow);
            if (due.Count > 0)
            {
                Notify($"Срок действия истёк или истекает в течение 7 дней: {due.Count}. Срок указан рядом с полем; изменить его можно в редакторе поля.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Notify("Не удалось создать резервную копию. Проверьте доступ к папке.", true);
        }
    }

    private void ConfigureLogin()
    {
        LoginHeading.Text = ViewModel.HasVault ? "С возвращением" : "Ваше личное хранилище";
        LoginDescription.Text = ViewModel.HasVault ? "Ваши секреты под замком. Введите мастер-пароль, чтобы продолжить." :
            "Придумайте мастер-пароль от 12 символов. Он защищает все данные. Восстановить забытый пароль нельзя.";
        LoginConfirmation.Visibility = ViewModel.HasVault ? Visibility.Collapsed : Visibility.Visible;
        UnlockButton.Content = ViewModel.HasVault ? "Открыть хранилище" : "Создать хранилище";
    }

    private void UpdateSession()
    {
        AppDiagnostics.Journal.Record(ViewModel.IsUnlocked ? AppEvent.Unlocked : AppEvent.Locked);
        epoch++;
        LoginView.Visibility = ViewModel.IsUnlocked ? Visibility.Collapsed : Visibility.Visible;
        VaultView.Visibility = ViewModel.IsUnlocked ? Visibility.Visible : Visibility.Collapsed;
        tray.Update(ViewModel.IsUnlocked);
        if (!ViewModel.IsUnlocked)
        {
            Notice.Hide();
            activeDialog?.Hide(); activeDialog = null;
            SettingsPanel.Visibility = Visibility.Collapsed;
            appearancePreview = null;
            LoginPassword.Password = ""; LoginConfirmation.Password = "";
            LoginError.IsOpen = false;
            _ = clipboard.ClearOwnedAsync();
            ShortcutSettings.Load(DevicePreferences.Load(AppPaths.DataDirectory).QuickAccess);
            ConfigureLogin();
            LoginPassword.Focus(FocusState.Programmatic);
            motion.Enter(LoginView);
        }
        else
        {
            LoginPassword.Password = ""; LoginConfirmation.Password = "";
            LoadSettings(); ApplyAppearance(); motion.Enter(VaultView);
            if (quickAccessWindow is null)
            {
                SearchBox.Focus(FocusState.Programmatic);
            }
        }
        UpdateEmptyState();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(ViewModel.CountLabel) or nameof(ViewModel.Query)) UpdateEmptyState(); }
    private void UpdateEmptyState()
    {
        EmptyState.Visibility = ViewModel.Associations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHeading.Text = string.IsNullOrWhiteSpace(ViewModel.Query) ? "Место для ваших секретов" : "Ничего не найдено";
        EmptyDescription.Text = string.IsNullOrWhiteSpace(ViewModel.Query) ?
            "Создайте ассоциацию для сайта или приложения, затем добавьте логин, пароль, ключи или файлы." :
            "Попробуйте другое название или начало слова. Поиск не учитывает регистр.";
    }

    private async Task Run(Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        if (actionBusy || exiting) return;
        actionBusy = true;
        AppDiagnostics.Journal.Record(AppEvent.ActionStarted, operation: operation);
        try { await action(); AppDiagnostics.Journal.Record(AppEvent.ActionCompleted, operation: operation); }
        catch (OperationCanceledException) { AppDiagnostics.Journal.Record(AppEvent.ActionCancelled, operation: operation); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or
            CryptographicException or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            AppDiagnostics.Journal.Record(AppEvent.ActionRejected, ex, operation);
            var message = ex is CryptographicException ? "Неверный пароль либо файл повреждён или изменён." : ex.Message;
            if (!exiting)
            {
                if (ViewModel.IsUnlocked) Notify(message, true);
                else { LoginError.Message = message; LoginError.IsOpen = true; }
            }
        }
        catch (Exception ex) { AppDiagnostics.Journal.Record(AppEvent.ActionFailed, ex, operation); throw; }
        finally { actionBusy = false; }
    }
    private void RequireSession(int captured)
    { if (!ViewModel.IsUnlocked || captured != epoch) throw new OperationCanceledException(); }
    private void Notify(string message, bool error = false)
    {
        if (exiting) return;
        ViewModel.Status = message;
        Notice.Show(message, error); motion.Enter(Notice);
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (ViewModel.IsBusy) return;
        UnlockButton.IsEnabled = false; LoginProgress.Visibility = Visibility.Visible; LoginProgress.IsActive = true; LoginError.IsOpen = false;
        try { await ViewModel.UnlockAsync(LoginPassword.Password, LoginConfirmation.Password); }
        finally { UnlockButton.IsEnabled = true; LoginProgress.IsActive = false; LoginProgress.Visibility = Visibility.Collapsed; LoginPassword.Password = ""; LoginConfirmation.Password = ""; }
    });
    private void LoginPassword_KeyDown(object sender, KeyRoutedEventArgs e)
    { if (e.Key == VirtualKey.Enter) { Unlock_Click(sender, new RoutedEventArgs()); e.Handled = true; } }

    private ContentDialog Dialog(string title, object content, string? primary = null)
    {
        var dialog = new ContentDialog
        {
        XamlRoot = Root.XamlRoot, Title = title, Content = content, PrimaryButtonText = primary ?? "",
        CloseButtonText = primary is null ? "Закрыть" : "Отмена", DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
        RequestedTheme = Root.ActualTheme
        };
        var resources = new ThemeResources();
        resources.Apply(ThemePalette.Resolve(appearancePreview ?? ViewModel.Settings, Root.ActualTheme == ElementTheme.Dark));
        dialog.Resources.MergedDictionaries.Add(resources);
        return dialog;
    }
    private async Task<ContentDialogResult> ShowDialog(ContentDialog dialog)
    {
        if (activeDialog is not null) throw new InvalidOperationException("Сначала закройте текущее окно.");
        activeDialog = dialog;
        void Decorate(object sender, RoutedEventArgs args)
        { if (!exiting && ReferenceEquals(activeDialog, dialog)) { ThemeService.ApplyTypography(dialog, appearancePreview ?? ViewModel.Settings); motion.Wire(dialog); } }
        dialog.Loaded += Decorate;
        try { return await dialog.ShowAsync(); }
        finally { dialog.Loaded -= Decorate; if (ReferenceEquals(activeDialog, dialog)) activeDialog = null; }
    }
    private async Task<bool> Confirm(string title, string text)
    {
        var captured = epoch;
        var result = await ShowDialog(Dialog(title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 }, "Удалить"));
        RequireSession(captured); return result == ContentDialogResult.Primary;
    }
    private static StackPanel Form() => new() { Spacing = 16, MinWidth = 360, MaxWidth = 520 };

    private async void AddAssociation_Click(object sender, RoutedEventArgs e) => await Run(() => EditAssociation(null));
    private async void RenameAssociation_Click(object sender, RoutedEventArgs e) => await Run(() => EditAssociation(((sender as Button)?.Tag as AssociationViewModel)?.Model));
    private async Task EditAssociation(SecretAssociation? existing)
    {
        var captured = epoch;
        RequireSession(captured);
        using var editor = new AssociationEditor(existing, ViewModel.TransferDocument);
        var dialog = Dialog(existing is null ? "Новая ассоциация" : "Редактирование ассоциации", editor, "Сохранить");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                RequireSession(captured);
                ViewModel.SaveAssociation(editor.BuildValue());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or OperationCanceledException)
            {
                editor.Error.Text = exception.Message;
                args.Cancel = true;
            }
        };
        var result = await ShowDialog(dialog);
        RequireSession(captured);
        if (result == ContentDialogResult.Primary)
        {
            Notify(existing is null ? "Ассоциация сохранена" : "Изменения сохранены");
        }
    }
    private async void DeleteAssociation_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if ((sender as Button)?.Tag is AssociationViewModel topic && await Confirm("Удалить ассоциацию?", $"«{topic.Title}» и все её поля будут удалены. Восстановление доступно в форме создания ассоциации."))
        { ViewModel.DeleteAssociation(topic.Model.Id); Notify("Ассоциация удалена"); }
    });

    private async void AddField_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    { if ((sender as Button)?.Tag is AssociationViewModel topic) await EditField(topic.Model.Id, null); });
    private Guid TopicFor(FieldViewModel field) => ViewModel.Associations.Single(a => a.Model.Fields.Any(f => f.Id == field.Model.Id)).Model.Id;
    private async void EditField_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    { if ((sender as Button)?.Tag is FieldViewModel field) await EditField(TopicFor(field), field.Model); });
    private async void DeleteField_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if ((sender as Button)?.Tag is not FieldViewModel field) return;
        var topic = TopicFor(field);
        if (await Confirm("Удалить поле?", $"Поле «{field.Description}» будет удалено целиком."))
        { ViewModel.DeleteField(topic, field.Model.Id); Notify("Поле удалено"); }
    });

    private async Task EditField(Guid topicId, SecretField? existing)
    {
        var captured = epoch;
        var panel = Form();
        var description = new TextBox { Header = "Описание", Text = existing?.Description ?? "", MaxLength = 200, PlaceholderText = "Например: логин, пароль или PublicKey" };
        var kind = new ComboBox { Header = "Тип значения", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var name in new[] { "Текст", "Секрет / пароль", "Изображение PNG / JPEG", "Любой файл" }) kind.Items.Add(name);
        var text = new TextBox { Header = "Значение", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 220, MaxLength = 1_000_000 };
        var secret = new PasswordBox { Header = "Значение секрета", MaxLength = 1_000_000, PasswordRevealMode = PasswordRevealMode.Peek };
        var generator = new Button { Content = "Сгенерировать пароль" };
        generator.Click += (_, _) => secret.Password = SecretManager.Core.Services.PasswordGenerator.Generate(ViewModel.Settings.PasswordGenerator);
        var expires = new CalendarDatePicker { Header = "Срок действия поля (необязательно)", Date = existing?.ExpiresAt };
        var clearExpiry = new Button { Content = "Без срока действия" };
        clearExpiry.Click += (_, _) => expires.Date = null;
        var audit = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        secret.PasswordChanged += (_, _) =>
        {
            if (ViewModel.IsUnlocked && captured == epoch && secret.Password.Length > 0)
            {
                var duplicates = ViewModel.TransferDocument.Associations.SelectMany(topic => topic.Fields)
                    .Count(field => field.Id != existing?.Id && field.Kind == FieldKind.Secret && field.Value == secret.Password);
                audit.Text = (secret.Password.Length < 12 ? "Короткое значение: меньше 12 символов. " : "") +
                    (duplicates > 0 ? $"Повторяется в других полях: {duplicates}." : "");
            }
            else
            {
                audit.Text = "";
            }

            audit.Visibility = kind.SelectedIndex == 1 && audit.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        var fieldId = existing?.Id ?? Guid.NewGuid();
        using var attachment = new AttachmentInput();
        attachment.Configure(appearancePreview ?? ViewModel.Settings, Root.ActualTheme == ElementTheme.Dark,
            () => ViewModel.IsUnlocked && captured == epoch);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed), Visibility = Visibility.Collapsed };
        error.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
        attachment.PickRequested += async () =>
        {
            try
            {
                var file = await FilePickerService.OpenAsync(AppWindow.Id, hwnd, "Добавить вложение",
                    kind.SelectedIndex == 2 ? [".png", ".jpg", ".jpeg"] : ["*"]); RequireSession(captured);
                if (file is null) return;
                await attachment.SetPathAsync(file); RequireSession(captured); error.Text = "";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            { error.Text = ex.Message; }
        };
        kind.SelectionChanged += async (_, _) =>
        {
            generator.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            audit.Visibility = kind.SelectedIndex == 1 && audit.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            await SwitchFieldKind(kind, text, secret, attachment);
        };
        panel.Children.Add(description); panel.Children.Add(kind); panel.Children.Add(text); panel.Children.Add(secret);
        panel.Children.Add(attachment); panel.Children.Add(error);
        panel.Children.Add(audit);
        panel.Children.Add(generator);
        panel.Children.Add(expires);
        panel.Children.Add(clearExpiry);
        kind.SelectedIndex = (int)(existing?.Kind ?? FieldKind.Secret);
        if (existing?.Kind == FieldKind.Text) text.Text = existing.Value;
        if (existing?.Kind == FieldKind.Secret) secret.Password = existing.Value;
        await SwitchFieldKind(kind, text, secret, attachment);
        if (existing?.Kind is FieldKind.Image or FieldKind.File) await attachment.SetExistingAsync(existing);
        RequireSession(captured);
        var dialog = Dialog(existing is null ? "Добавить поле" : "Редактирование поля", new ScrollViewer { Content = panel, MaxHeight = 540 }, "Сохранить");
        var history = new ComboBox { Header = existing is null ? "Удалённые поля этой ассоциации" : "История этого поля", HorizontalAlignment = HorizontalAlignment.Stretch };
        var activeIds = ViewModel.TransferDocument.Associations.Single(topic => topic.Id == topicId).Fields.Select(field => field.Id).ToHashSet();
        history.ItemsSource = ViewModel.TransferDocument.Revisions.Where(revision => revision.Association.Id == topicId)
            .OrderByDescending(revision => revision.CreatedAt).SelectMany(revision => revision.Association.Fields
                .Where(field => existing is null ? !activeIds.Contains(field.Id) : field.Id == existing.Id)
                .Select(field => new ComboBoxItem { Tag = field, Content = $"{revision.CreatedAt.ToLocalTime():g} · {field.Description}" })).ToArray();
        var historyBody = new StackPanel { Spacing = 8 };
        historyBody.Children.Add(history);
        historyBody.Children.Add(new TextBlock { Text = "Значение подставится в форму. Примените его кнопкой «Сохранить».", TextWrapping = TextWrapping.Wrap });
        var restore = new Button { Content = "Подставить версию поля" };
        restore.Click += async (_, _) =>
        {
            if (!ReferenceEquals(activeDialog, dialog) || !ViewModel.IsUnlocked || captured != epoch ||
                history.SelectedItem is not ComboBoxItem { Tag: SecretField selected })
            {
                return;
            }

            try
            {
                fieldId = selected.Id;
                expires.Date = selected.ExpiresAt;
                description.Text = selected.Description;
                text.Text = selected.Kind == FieldKind.Text ? selected.Value : "";
                secret.Password = selected.Kind == FieldKind.Secret ? selected.Value : "";
                kind.SelectedIndex = (int)selected.Kind;
                await SwitchFieldKind(kind, text, secret, attachment);
                if (selected.Kind is FieldKind.Image or FieldKind.File && ReferenceEquals(activeDialog, dialog))
                {
                    await attachment.SetExistingAsync(selected);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                if (ReferenceEquals(activeDialog, dialog))
                {
                    error.Text = exception.Message;
                }
            }
        };
        historyBody.Children.Add(restore);
        panel.Children.Add(new Expander { Header = existing is null ? "Восстановить удалённое поле" : "История изменений", Content = historyBody,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        attachment.StateChanged += () => dialog.IsPrimaryButtonEnabled = !attachment.IsBusy;
        using var pasteShortcut = new AttachmentPasteHandler(dialog, attachment, async images =>
        { kind.SelectedIndex = images ? 2 : 3; await SwitchFieldKind(kind, text, secret, attachment); },
            () => ViewModel.IsUnlocked && captured == epoch, message => error.Text = message);
        attachment.PasteRequested += async () => await pasteShortcut.PasteFromClipboardAsync();
        SecretField? saved = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                RequireSession(captured);
                var fieldKind = (FieldKind)kind.SelectedIndex;
                if (fieldKind is FieldKind.Image or FieldKind.File && (attachment.IsBusy || !attachment.IsValidForCurrentMode))
                    throw new ArgumentException(attachment.IsBusy ? "Дождитесь загрузки вложения." : "Выберите подходящий файл или изображение.");
                var value = fieldKind switch { FieldKind.Text => text.Text, FieldKind.Secret => secret.Password, _ => attachment.Attachment?.Base64 ?? throw new ArgumentException("Выберите файл.") };
                saved = new SecretField { Id = fieldId, ExpiresAt = expires.Date, Description = description.Text.Trim(), Kind = fieldKind,
                    Value = value, FileName = fieldKind is FieldKind.File or FieldKind.Image ? attachment.Attachment!.FileName : null,
                    ContentType = fieldKind is FieldKind.File or FieldKind.Image ? attachment.Attachment!.ContentType : null };
                VaultValidation.Validate(new() { Associations = [new() { Title = "Проверка", Fields = [saved] }] });
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or OperationCanceledException)
            { error.Text = ex.Message; args.Cancel = true; }
        };
        try
        {
            var result = await ShowDialog(dialog); RequireSession(captured);
            if (result == ContentDialogResult.Primary && saved is not null)
            { ViewModel.SaveField(topicId, saved); Notify("Поле сохранено"); }
        }
        finally
        {
            text.Text = "";
            secret.Password = "";
            description.Text = "";
            history.ItemsSource = null;
            panel.Children.Clear();
            saved = null;
        }
    }

    private static async Task SwitchFieldKind(ComboBox kind, TextBox text, PasswordBox secret, AttachmentInput attachment)
    {
        text.Visibility = kind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        secret.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        attachment.Visibility = kind.SelectedIndex >= 2 ? Visibility.Visible : Visibility.Collapsed;
        await attachment.SetImageModeAsync(kind.SelectedIndex == 2);
    }

    private async void Copy_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    { if ((sender as Button)?.Tag is FieldViewModel field) await CopyField(field); });
    private async void Value_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if ((sender as Button)?.Tag is not FieldViewModel field) return;
        await CopyField(field); if (field.IsImage) await Preview(field);
    });
    private async Task CopyField(FieldViewModel field)
    {
        var captured = epoch; RequireSession(captured);
        await clipboard.CopyAsync(field.Model, ViewModel.Settings.ClipboardClearSeconds, () => ViewModel.IsUnlocked && captured == epoch);
        RequireSession(captured);
        Notify(ViewModel.Settings.ClipboardClearSeconds > 0 ? $"Скопировано. Буфер очистится через {ViewModel.Settings.ClipboardClearSeconds} с." : "Скопировано в буфер обмена");
    }
    private async void Preview_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    { if ((sender as Button)?.Tag is FieldViewModel { IsImage: true } field) { await CopyField(field); await Preview(field); } });
    private async Task Preview(FieldViewModel field)
    {
        var captured = epoch;
        var bytes = Convert.FromBase64String(field.Model.Value);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); }
        CryptographicOperations.ZeroMemory(bytes); stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream); RequireSession(captured);
        if ((long)decoder.PixelWidth * decoder.PixelHeight > 40_000_000) throw new InvalidDataException("Изображение слишком велико для предпросмотра (более 40 Мп).");
        stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); RequireSession(captured);
        var image = new Microsoft.UI.Xaml.Controls.Image { Source = bitmap, MaxWidth = 760, MaxHeight = 560, Stretch = Stretch.Uniform };
        var dialog = Dialog(field.Model.FileName ?? "Предпросмотр", image);
        dialog.Resources["ContentDialogMaxWidth"] = 850d;
        try { await ShowDialog(dialog); }
        finally { image.Source = null; }
    }

    private async Task<string?> AskPassword(string title, bool confirm)
    {
        var captured = epoch;
        var panel = Form();
        var explanation = new TextBlock { Text = confirm ? "Задайте отдельный пароль от 8 символов. Он понадобится для импорта на другом устройстве." : "Введите пароль, заданный при экспорте файла.", TextWrapping = TextWrapping.Wrap, Opacity = .65 };
        var password = new PasswordBox { Header = "Пароль экспорта" };
        var repeat = new PasswordBox { Header = "Повторите пароль", Visibility = confirm ? Visibility.Visible : Visibility.Collapsed };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(explanation); panel.Children.Add(password); panel.Children.Add(repeat); panel.Children.Add(error);
        var dialog = Dialog(title, panel, "Продолжить");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { if (confirm) { VaultCrypto.ValidateExportPassword(password.Password); if (password.Password != repeat.Password) throw new ArgumentException("Пароли не совпадают."); } }
            catch (ArgumentException ex) { error.Text = ex.Message; args.Cancel = true; }
        };
        try { var result = await ShowDialog(dialog); RequireSession(captured); return result == ContentDialogResult.Primary ? password.Password : null; }
        finally { password.Password = ""; repeat.Password = ""; }
    }
    private async void Export_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var captured = epoch;
        var selection = await AskSelection(ViewModel.TransferDocument, "Что экспортировать?"); RequireSession(captured);
        if (selection is null) return;
        var password = await AskPassword("Зашифрованная резервная копия", true);
        if (password is null) return;
        var encrypted = await ViewModel.ExportAsync(password, selection); password = ""; RequireSession(captured);
        var file = await FilePickerService.SaveAsync(AppWindow.Id, hwnd); RequireSession(captured); if (file is null) return;
        await File.WriteAllTextAsync(file, encrypted); RequireSession(captured);
        Notify("Зашифрованная копия сохранена. SHA-256 и AES-GCM защищают целостность.");
    });

    private void ExportMenu_Opening(object sender, object args)
    {
        AutomaticBackupsItem.IsChecked = ViewModel.Settings.AutomaticBackups;
    }

    private async void AutomaticBackups_Click(object sender, RoutedEventArgs args)
    {
        await Run(() =>
        {
            RequireSession(epoch);
            ViewModel.SaveSettings(ViewModel.Settings with { AutomaticBackups = AutomaticBackupsItem.IsChecked });
            return Task.CompletedTask;
        });
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs args)
    {
        await Run(() =>
        {
            RequireSession(epoch);
            ViewModel.Backup(true);
            Notify("Зашифрованная резервная копия создана");
            return Task.CompletedTask;
        });
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs args)
    {
        await Run(async () =>
        {
            var captured = epoch;
            RequireSession(captured);
            using var panel = new VaultBackupPanel(ViewModel, () => ViewModel.IsUnlocked && captured == epoch && !exiting);
            await ShowDialog(Dialog("Восстановление резервной копии", panel));
        });
    }
    private async void Import_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var captured = epoch;
        var file = await FilePickerService.OpenAsync(AppWindow.Id, hwnd, "Импорт хранилища", ".smvault"); RequireSession(captured); if (file is null) return;
        string json;
        using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            json = await ExportPackage.ReadTextAsync(input);
        RequireSession(captured);
        _ = ExportPackage.Read(json); // Check SHA-256 before asking for a password or changing any local data.
        var password = await AskPassword("Импорт резервной копии", false); if (password is null) return;
        var imported = await Task.Run(() => VaultSession.ReadExport(json, password)); password = ""; RequireSession(captured);
        var selection = await AskSelection(imported, "Что импортировать?"); RequireSession(captured);
        if (selection is null) return;
        imported = VaultSelection.Select(imported, selection);
        var choices = new Dictionary<Guid, ConflictResolution>();
        foreach (var conflict in ViewModel.FindConflicts(imported))
        {
            var panel = Form();
            panel.Children.Add(new TextBlock { Text = $"Ассоциация «{conflict.Local.Title}» уже существует и отличается.\nЛокально: {conflict.Local.Fields.Count} полей, {conflict.Local.UpdatedAt.ToLocalTime():g}\nВ файле: {conflict.Imported.Fields.Count} полей, {conflict.Imported.UpdatedAt.ToLocalTime():g}", TextWrapping = TextWrapping.Wrap });
            var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var name in new[] { "Оставить локальную", imported.IsPartial ? "Обновить выбранные поля" : "Заменить импортированной", "Сохранить обе" }) choice.Items.Add(name);
            choice.SelectedIndex = 0; panel.Children.Add(choice);
            var result = await ShowDialog(Dialog("Конфликт импорта", panel, "Продолжить")); RequireSession(captured);
            if (result != ContentDialogResult.Primary) return; // No writes until every conflict has been resolved.
            choices[conflict.Imported.Id] = (ConflictResolution)choice.SelectedIndex;
        }
        ViewModel.Import(imported, choices); Notify("Импорт завершён. Ассоциации и поля объединены, настройки устройства сохранены.");
    });

    private async Task<Dictionary<Guid, IReadOnlySet<Guid>>?> AskSelection(VaultDocument document, string title)
    {
        var captured = epoch;
        using var selection = new TransferSelection(document);
        var dialog = Dialog(title, selection, "Продолжить");
        selection.SelectionChanged += () => dialog.IsPrimaryButtonEnabled = selection.HasSelection;
        dialog.PrimaryButtonClick += (_, args) => { if (!selection.HasSelection) args.Cancel = true; };
        var result = await ShowDialog(dialog); RequireSession(captured);
        return result == ContentDialogResult.Primary ? selection.Capture() : null;
    }

    private void LoadSettings()
    {
        loadingSettings = true;
        var s = ViewModel.Settings; accent = s.AccentColor; background = s.BackgroundColor;
        ThemeChoice.SelectedIndex = (int)s.Theme; FontChoice.SelectedItem = s.FontFamily;
        FontSizeChoice.Value = s.FontSize; RadiusChoice.Value = s.CornerRadius; CompactChoice.IsOn = s.CompactLayout;
        SingleExpandedChoice.IsOn = s.SingleExpandedAssociation;
        AnimationChoice.SelectedIndex = (int)s.AnimationLevel; MotionSpeedChoice.Value = s.MotionSpeed;
        LockMinutesChoice.Value = s.AutoLockMinutes; ClipboardSecondsChoice.Value = s.ClipboardClearSeconds;
        StorageLimitChoice.Value = s.MaxStorageMegabytes;
        TrayChoice.IsOn = s.CloseToTray; LockHiddenChoice.IsOn = s.LockWhenHidden;
        StartupChoice.IsOn = startupRegistration.Enabled;
        AutoUpdateChoice.IsOn = DevicePreferences.Load(AppPaths.DataDirectory).AutomaticUpdates;
        loadingSettings = false;
    }
    private AppSettings ReadSettings() => ViewModel.Settings with
    {
        Theme = (AppTheme)Math.Max(0, ThemeChoice.SelectedIndex), FontFamily = FontChoice.SelectedItem as string ?? "Segoe UI Variable",
        FontSize = double.IsFinite(FontSizeChoice.Value) ? FontSizeChoice.Value : 14,
        CornerRadius = RadiusChoice.Value, CompactLayout = CompactChoice.IsOn,
        SingleExpandedAssociation = SingleExpandedChoice.IsOn,
        AnimationLevel = (AnimationLevel)Math.Max(0, AnimationChoice.SelectedIndex), MotionSpeed = MotionSpeedChoice.Value,
        AccentColor = accent, BackgroundColor = background,
        AutoLockMinutes = double.IsFinite(LockMinutesChoice.Value) ? (int)LockMinutesChoice.Value : 5,
        ClipboardClearSeconds = double.IsFinite(ClipboardSecondsChoice.Value) ? (int)ClipboardSecondsChoice.Value : 30,
        MaxStorageMegabytes = double.IsFinite(StorageLimitChoice.Value) ? (int)StorageLimitChoice.Value : 64,
        CloseToTray = TrayChoice.IsOn, LockWhenHidden = LockHiddenChoice.IsOn
    };
    private void ApplyAppearance()
    {
        if (applyingAppearance || exiting) return;
        applyingAppearance = true;
        try
        {
        AppDiagnostics.Journal.Record(AppEvent.AppearanceApplying);
        var settings = appearancePreview ?? ViewModel.Settings;
        var palette = ThemeService.Apply(Root, settings);
        Navigation.OpenPaneLength = Math.Clamp(218 * settings.FontSize / 14, 218, 360);
        SettingsPanel.Width = Math.Clamp(360 * settings.FontSize / 20, 360, 480);
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = ThemeService.ParseColor(palette.Foreground);
        titleBar.ButtonInactiveForegroundColor = ThemeService.ParseColor(palette.Secondary);
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = ThemeService.ParseColor(palette.ControlHover);
        titleBar.ButtonHoverForegroundColor = ThemeService.ParseColor(palette.Foreground);
        titleBar.ButtonPressedBackgroundColor = ThemeService.ParseColor(palette.Control);
        titleBar.ButtonPressedForegroundColor = ThemeService.ParseColor(palette.Foreground);
        motion.Reset(Root); motion.Wire(Root);
        AppDiagnostics.Journal.Record(AppEvent.AppearanceApplied);
        }
        finally { applyingAppearance = false; }
    }
    private void PreviewAppearance()
    {
        if (loadingSettings || !ViewModel.IsUnlocked) return;
        appearancePreview = ReadSettings(); ApplyAppearance();
    }
    private void Appearance_Changed(object sender, SelectionChangedEventArgs e) => PreviewAppearance();
    private void AppearanceSlider_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => PreviewAppearance();
    private void AppearanceToggle_Changed(object sender, RoutedEventArgs e) => PreviewAppearance();
    private async void AccentColor_Click(object sender, RoutedEventArgs e) => await Run(() => PickColor(true));
    private async void BackgroundColor_Click(object sender, RoutedEventArgs e) => await Run(() => PickColor(false));
    private async Task PickColor(bool isAccent)
    {
        var captured = epoch;
        var picker = new ColorPicker { Color = ThemeService.ParseColor(isAccent ? accent : background), IsAlphaEnabled = false, IsColorSliderVisible = true, IsHexInputVisible = true, IsMoreButtonVisible = false };
        if (await ShowDialog(Dialog(isAccent ? "Цвет акцента" : "Цвет фона", picker, "Применить")) != ContentDialogResult.Primary) return;
        RequireSession(captured);
        if (isAccent) accent = ThemeService.ToHex(picker.Color); else background = ThemeService.ToHex(picker.Color);
        ThemeChoice.SelectedIndex = (int)AppTheme.Custom; PreviewAppearance();
    }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e) => await Run(() =>
    {
        ViewModel.SaveSettings(ReadSettings()); SaveDevicePreferences(); appearancePreview = null; ApplyAppearance(); Notify("Настройки сохранены"); return Task.CompletedTask;
    });
    private async void OpenDiagnostics_Click(object sender, RoutedEventArgs e) => await Run(() =>
    {
        var folder = AppDiagnostics.Journal.DirectoryPath; Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        return Task.CompletedTask;
    });
    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        loadingSettings = true; var defaults = new AppSettings();
        ThemeChoice.SelectedIndex = 0; FontChoice.SelectedItem = defaults.FontFamily; FontSizeChoice.Value = defaults.FontSize;
        RadiusChoice.Value = defaults.CornerRadius; CompactChoice.IsOn = false;
        SingleExpandedChoice.IsOn = defaults.SingleExpandedAssociation;
        AnimationChoice.SelectedIndex = (int)defaults.AnimationLevel; MotionSpeedChoice.Value = 1;
        accent = defaults.AccentColor; background = defaults.BackgroundColor; loadingSettings = false; PreviewAppearance();
    }
    private async void ChangePassword_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var captured = epoch;
        var panel = Form(); var old = new PasswordBox { Header = "Текущий пароль" }; var next = new PasswordBox { Header = "Новый пароль · от 12 символов" }; var repeat = new PasswordBox { Header = "Повторите новый пароль" };
        panel.Children.Add(old); panel.Children.Add(next); panel.Children.Add(repeat);
        try
        {
            var result = await ShowDialog(Dialog("Смена мастер-пароля", panel, "Сменить пароль")); RequireSession(captured);
            if (result != ContentDialogResult.Primary) return;
            if (next.Password != repeat.Password) throw new ArgumentException("Новые пароли не совпадают.");
            await ViewModel.ChangePasswordAsync(old.Password, next.Password); RequireSession(captured); Notify("Мастер-пароль изменён");
        }
        finally { old.Password = ""; next.Password = ""; repeat.Password = ""; }
    });

    private void Menu_Click(object sender, RoutedEventArgs e) { Navigation.IsPaneOpen = !Navigation.IsPaneOpen; motion.Enter(AssociationList); }
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();
    private void All_Click(object sender, RoutedEventArgs e) { ViewModel.Query = ""; foreach (var a in ViewModel.Associations) a.IsExpanded = false; }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var opening = SettingsPanel.Visibility != Visibility.Visible;
        AppDiagnostics.Journal.Record(opening ? AppEvent.SettingsOpened : AppEvent.SettingsClosed);
        if (opening) { LoadSettings(); navigationWasOpen = Navigation.IsPaneOpen; Navigation.IsPaneOpen = false; SettingsPanel.Visibility = Visibility.Visible; motion.Enter(SettingsPanel); }
        else { SettingsPanel.Visibility = Visibility.Collapsed; Navigation.IsPaneOpen = navigationWasOpen; appearancePreview = null; ApplyAppearance(); }
    }
    private void QuickNavigation_ItemClick(object sender, ItemClickEventArgs e)
    { if (e.ClickedItem is AssociationViewModel topic) { topic.IsExpanded = true; AssociationList.ScrollIntoView(topic); } }
    private void Card_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        ThemeService.ApplyTypography(element, appearancePreview ?? ViewModel.Settings); motion.Wire(element);
        var item = element.DataContext as AssociationViewModel;
        motion.Enter(element, item is null ? 0 : ViewModel.Associations.IndexOf(item));
    }

    private async void Help_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var text = new TextBlock
        {
            MaxWidth = 620, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Text = "БЫСТРЫЙ СТАРТ\n\n1. Создайте мастер-пароль от 12 символов. Сохраните его в надёжном месте.\n2. Нажмите «Новая ассоциация»: например, GitHub или почта.\n3. Раскройте карточку → «Добавить поле». Полей может быть сколько угодно: логин, пароль, PublicKey, заметка, изображение или файл. В зоне вложения можно перетащить один файл. Для картинки или файла доступны «Вставить из буфера» и Ctrl+V во всей форме. При вставке вложения тип выбирается автоматически; обычный текст вставляется в активное текстовое поле.\n4. Нажмите значение или кнопку копирования. Картинка одновременно копируется и открывается крупно.\n5. В меню слева → «Экспорт»: выберите ассоциации и поля, задайте отдельный пароль от 8 символов и сохраните зашифрованный .smvault. На другом компьютере создайте локальное хранилище → «Импорт». После ввода пароля можно выбрать только нужные ассоциации и поля.\n\nПОИСК\n\nИщет только названия, без учёта регистра. Можно вводить начало слова или несколько слов. Достаточно совпадения любого из них: «Почта Mail» найдёт и «Почта 1», и «Mail». Кириллица поддерживается. В настройках можно включить раскрытие только одной карточки. Значения полей в поиск не попадают.\n\nКАК ЗАЩИЩЕНЫ ДАННЫЕ?\n\nSQLite содержит зашифрованные названия, описания, значения и вложения. Файлы сначала преобразуются в base64, затем шифруются. Base64 не является защитой. Экспорт проверяется по SHA-256, затем по тегу AES-GCM: пересчёт хешсуммы не позволяет подменить данные.\n\nЧТО ДЕЛАЕТ ИМПОРТ?\n\nОбъединяет ассоциации. При совпадении идентификатора или названия предложит оставить локальную, заменить её или сохранить обе. До разрешения всех конфликтов база не меняется. В частичном переносе «Обновить выбранные поля» сохраняет остальные локальные поля. Поля сопоставляются по идентификатору; разные поля с одинаковым описанием не перезаписываются автоматически. Частичные копии требуют SecretManager 0.1.5 или новее; старые версии откажутся открывать их. Полные копии прежних версий поддерживаются. Настройки внешнего вида и блокировки устройства сохраняются локальными.\n\nЗАБЫЛ МАСТЕР-ПАРОЛЬ\n\nВосстановления нет. Создайте хранилище на другом устройстве и импортируйте резервную копию с известным паролем экспорта. Регулярно делайте резервные копии.\n\nБЛОКИРОВКА И ТРЕЙ\n\nCtrl+L блокирует сразу, Ctrl+F переводит фокус в поиск. По умолчанию окно блокируется после 5 минут без фокуса. Крестик скрывает окно в трей и блокирует сразу. Кнопка «Выйти» в левом меню завершает приложение полностью; верхний замок и Ctrl+L только блокируют. Значок: жёлтый замок — заблокировано, зелёный — открыто. Левый клик возвращает окно; правый клик → «Выйти» завершает процесс. Таймеры и поведение крестика настраиваются, 0 отключает таймер. Блокировка Windows также блокирует хранилище.\n\nБУФЕР И ФАЙЛЫ\n\nБуфер очищается через 30 секунд, только если в нём остались наши данные. История и облачный буфер Windows отключены для этих копирований. При блокировке данные приложения в буфере очищаются. Другие программы могут успеть прочитать буфер раньше. Для копирования произвольного файла создаётся временный файл в LocalAppData, который удаляется при очистке. Вложение: до 10 МБ, снимок: до 64 МБ. Предпросмотр: PNG/JPEG до 40 Мп. Для выбора файла используется настольный диалог с резервным стандартным диалогом. При запуске от администратора Windows ограничивает drag-and-drop: используйте кнопку или перезапустите приложение без повышенных прав.\n\nТЕМЫ И АНИМАЦИИ\n\nНастройки справа: системная, светлая, тёмная, полночь, лесная или собственная тема. Собственная тема позволяет выбрать фон и акцент. Шрифт, размер 10–26 пунктов, скругления и плотность доступны во всех темах. Размер задаётся ползунком; доступен Comic Sans MS. Изменения оформления видны сразу; «Сохранить настройки» закрепляет их. Закрытие панели отменяет несохранённый предпросмотр. Декоративные анимации: выключены, умеренные, максимальные. Скорость регулируется у подсветки и пружинного отклика при наведении и нажатии. Раскрытие и сворачивание карточек, появление содержимого и стандартные состояния элементов имеют постоянный темп. Системное отключение движения имеет приоритет.\n\nЖУРНАЛ ОШИБОК\n\nНастройки → «Открыть папку журналов». Файлы application.log и application.previous.log содержат время UTC, версию, названия операций, типы и коды ошибок, а для HTTP — численный статус ответа. Содержимое хранилища и сообщения исключений не записываются. Запись PreviousRunInterrupted при следующем запуске означает незавершённый предыдущий сеанс; это также возможно после отключения питания или принудительного завершения. Нативные аварии дополнительно попадают в журнал приложений Windows.\n\nАВТОЗАПУСК И ОБНОВЛЕНИЯ\n\nВ настройках можно включить запуск при входе в Windows. Приложение запускается заблокированным в трее; открывается по значку замка. Автообновление включено по умолчанию для установленной программы: при запуске читается SHA-файл из GitHub Releases напрямую, без GitHub API. Он содержит версию, размер и SHA-256 установщика. SHA-256, размер и версия EXE проверяются до запуска. Старый релиз без версии или отсутствие файла показываются отдельным сообщением. Установка ждёт блокировки хранилища и завершения текущего процесса, затем приложение запускается снова. Можно установить готовое обновление кнопкой или отключить автообновление. Без сети приложение продолжает работать. Для ручной проверки есть кнопка в настройках. Из сборки для разработки автоустановка не выполняется. Ошибка установки записывается в Updates внутри папки данных, в update-result.txt.\n\nГДЕ ДАННЫЕ?\n\n%LocalAppData%\\SecretManager\\vault.db. Удаление программы сохраняет хранилище. Приложение работает локально и не отправляет ваши секреты в сеть. Почтового входа нет."
        };
        var dialog = Dialog("FAQ", new ScrollViewer { Content = text, MaxHeight = 580 });
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        await ShowDialog(dialog);
    });

    private void CheckFocusTimeout()
    { if (!exiting && ViewModel.IsUnlocked && focus.ShouldLock(DateTimeOffset.UtcNow, ViewModel.Settings.AutoLockMinutes)) ViewModel.Lock(); }
    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (exiting) return;
        var focused = args.WindowActivationState != WindowActivationState.Deactivated;
        if (focused) CheckFocusTimeout(); // Check before resetting elapsed unfocused time.
        isFocused = focused;
        focus.SetFocused(focused || quickAccessWindow?.IsActive == true, DateTimeOffset.UtcNow);
    }
    private void ShowWindow()
    { if (exiting) return; AppDiagnostics.Journal.Record(AppEvent.WindowShown); CheckFocusTimeout(); AppWindow.Show(); NativeMethods.Show(hwnd); Activate(); }
    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!exiting && ViewModel.Settings.CloseToTray)
        {
            args.Cancel = true;
            AppDiagnostics.Journal.Record(AppEvent.WindowHidden);
            if (ViewModel.Settings.LockWhenHidden) ViewModel.Lock();
            focus.SetFocused(false, DateTimeOffset.UtcNow); isFocused = false; AppWindow.Hide();
        }
        else if (!exiting) { args.Cancel = true; ExitApplication(); }
    }
    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect)
            DispatcherQueue.TryEnqueue(() => { if (!exiting) ViewModel.Lock(); });
    }
    private async void ExitApplication()
    {
        if (exiting) return;
        exiting = true;
        AppDiagnostics.Journal.Record(AppEvent.ExitRequested);
        ViewModel.Lock();
        await clipboard.ClearOwnedAsync();
        // Keep the dispatcher alive until password verification and its binding
        // notifications have completed, so their continuation cannot target a dead UI.
        while (ViewModel.IsBusy) await Task.Delay(20);
        // WinUI #11653: a focused text/password input can crash framework teardown.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        { UnlockButton.IsEnabled = true; UnlockButton.Focus(FocusState.Programmatic); Close(); });
    }
    private void Window_Closed(object sender, WindowEventArgs args)
    {
        quickAccessWindow?.Close();
        globalHotkey?.Dispose();
        timer.Tick -= MaintenanceTick;
        exiting = true;
        updateCancellation.Cancel(); updateClient?.Dispose();
        updateNoticeTimer.Stop(); updateNoticeTimer.Tick -= DismissUpdateNotice;
        timer.Stop(); SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        ViewModel.SessionChanged -= UpdateSession; ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        activeDialog?.Hide(); ViewModel.Lock(); ViewModel.Dispose();
        _ = clipboard.ClearOwnedAsync(); clipboard.Dispose(); tray.Dispose();
        // The last Window.Close ends the application; do not start XAML shutdown twice.
    }
}
