using System.Net.Http;
using System.Threading;
using SecretManager.App.Services;

namespace SecretManager.App;

public sealed partial class MainWindow
{
    private readonly StartupRegistration startupRegistration = new(Environment.ProcessPath ?? "");
    private readonly CancellationTokenSource updateCancellation = new();
    private HttpClient? updateClient;
    private PreparedUpdate? preparedUpdate;
    private bool checkingUpdate;
    private bool windowsServicesStarted;
    private bool automaticUpdates;

    public void StartWindowsServices(bool startInTray)
    {
        windowsServicesStarted = true;
        automaticUpdates = DevicePreferences.Load(AppPaths.DataDirectory).AutomaticUpdates;
        if (startInTray)
        {
            ViewModel.Lock(); AppWindow.Hide();
            if (!Root.IsLoaded) Root.Loaded += HideAfterStartupLoad;
        }
        if (automaticUpdates && IsInstalledApplication()) _ = CheckUpdatesAsync(false);
    }
    private void HideAfterStartupLoad(object sender, RoutedEventArgs args)
    { Root.Loaded -= HideAfterStartupLoad; if (!exiting) AppWindow.Hide(); }
    private static bool IsInstalledApplication() =>
        string.Equals(Path.GetFileName(Environment.ProcessPath), "SecretManager.App.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe")) &&
        Environment.GetEnvironmentVariable("SECRETMANAGER_DATA_DIR") is null;

    private void SaveDevicePreferences()
    {
        if (startupRegistration.Enabled != StartupChoice.IsOn) startupRegistration.SetEnabled(StartupChoice.IsOn);
        new DevicePreferences(AutoUpdateChoice.IsOn).Save(AppPaths.DataDirectory);
        automaticUpdates = AutoUpdateChoice.IsOn;
    }
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (checkingUpdate || exiting || preparedUpdate is not null) return;
        checkingUpdate = true;
        try
        {
            if (interactive) ShowUpdateMessage("Проверка обновлений…");
            updateClient ??= UpdateService.CreateClient(); var updater = new UpdateService(updateClient);
            var release = await updater.CheckAsync(UpdateService.CurrentVersion, updateCancellation.Token);
            if (exiting) return;
            if (release is null) { if (interactive) ShowUpdateMessage("Новая версия не найдена."); return; }
            if (!IsInstalledApplication())
            {
                ShowUpdateMessage($"Доступна версия {release.Version}. Автоустановка доступна для установленной программы; установщик — в релизах GitHub."); return;
            }
            ShowUpdateMessage($"Загрузка версии {release.Version}… Приложением можно пользоваться.");
            var downloaded = await updater.DownloadAsync(release, AppPaths.DataDirectory, updateCancellation.Token);
            if (exiting) return;
            UpdateService.ValidateInstaller(downloaded); preparedUpdate = downloaded;
            ShowUpdateMessage($"Версия {release.Version} готова. После блокировки приложение установит её и перезапустится.");
            InstallUpdateButton.Visibility = Visibility.Visible;
            TryApplyAutomaticUpdate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or
            InvalidOperationException or System.Security.Cryptography.CryptographicException or ArgumentException or KeyNotFoundException or FormatException or System.ComponentModel.Win32Exception)
        {
            AppDiagnostics.Journal.Record(AppEvent.ActionRejected, ex, "CheckUpdates");
            if (interactive && !exiting) ShowUpdateMessage("Не удалось проверить или загрузить обновление. Повторите позже; приложение продолжает работать.", true);
            else if (!exiting) UpdateNotice.IsOpen = false;
        }
        finally { checkingUpdate = false; }
    }
    private void ShowUpdateMessage(string message, bool error = false)
    {
        if (exiting) return;
        UpdateNotice.Message = message;
        UpdateNotice.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational; UpdateNotice.IsOpen = true;
    }
    private void TryApplyAutomaticUpdate()
    {
        if (windowsServicesStarted && automaticUpdates && preparedUpdate is not null && !exiting && !actionBusy &&
            !ViewModel.IsUnlocked && !ViewModel.IsBusy && activeDialog is null) ApplyPreparedUpdate();
    }
    private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (preparedUpdate is null) return;
        if (ViewModel.IsUnlocked)
        {
            var result = await ShowDialog(Dialog("Установить обновление?", new TextBlock
            { Text = "Хранилище будет заблокировано. Приложение закроется, установит обновление и откроется снова.", TextWrapping = TextWrapping.Wrap, MaxWidth = 440 }, "Установить"));
            if (result != ContentDialogResult.Primary || exiting) return;
        }
        ViewModel.Lock(); ApplyPreparedUpdate();
    });
    private void ApplyPreparedUpdate()
    {
        if (preparedUpdate is not { } update || exiting) return;
        try
        {
            UpdateService.LaunchInstallerAfterExit(update);
            preparedUpdate = null; InstallUpdateButton.Visibility = Visibility.Collapsed; ExitApplication();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            automaticUpdates = false; AppDiagnostics.Journal.Record(AppEvent.ActionRejected, ex, "InstallUpdate");
            ShowUpdateMessage("Не удалось запустить установку. Можно повторить кнопкой или установить обновление вручную из GitHub Releases.", true);
        }
    }
}
