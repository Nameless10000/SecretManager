using System.Threading;
using SecretManager.App.Services;

namespace SecretManager.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Mutex? instance;
    private bool primaryInstance;
    public MainWindow? MainWindow { get; private set; }
    public App()
    {
        UnhandledException += (_, args) => AppDiagnostics.Journal.Record(AppEvent.UnhandledException, args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppDiagnostics.Journal.Record(AppEvent.UnhandledException, args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => AppDiagnostics.Journal.Record(AppEvent.UnobservedTask, args.Exception);
        // Native fail-fast does not necessarily reach managed exception handlers.
        // Leave run.pending until normal CLR process exit, after WinUI teardown.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (primaryInstance) AppDiagnostics.Journal.StopCleanly(); };
        try { InitializeComponent(); }
        catch (Exception ex) { AppDiagnostics.Journal.Record(AppEvent.StartupFailed, ex); throw; }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A second process would have its own stale decrypted snapshot: forbid it.
        instance = new Mutex(true, "Local\\SecretManager.6D0F5C26", out var first);
        if (!first)
        {
            NativeMethods.ActivateExistingWindow();
            instance.Dispose(); instance = null;
            Exit(); return;
        }
        primaryInstance = true; AppDiagnostics.Journal.Start();
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
            MainWindow.StartWindowsServices(Environment.GetCommandLineArgs().Contains("--startup"));
            MainWindow.Closed += (_, _) => { instance?.ReleaseMutex(); instance?.Dispose(); instance = null; };
        }
        catch (Exception ex) { AppDiagnostics.Journal.Record(AppEvent.StartupFailed, ex); throw; }
    }
}
