using System.Threading;
using SecretManager.App.Services;

namespace SecretManager.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Mutex? instance;
    public MainWindow? MainWindow { get; private set; }
    public App()
    {
        UnhandledException += (_, args) => WriteStartupError(args.Exception, args.Message);
        try { InitializeComponent(); }
        catch (Exception ex) { WriteStartupError(ex); throw; }
    }
    private static void WriteStartupError(Exception exception, string? detail = null)
    {
        try
        {
            var folder = Environment.GetEnvironmentVariable("SECRETMANAGER_DATA_DIR") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretManager");
            Directory.CreateDirectory(folder);
            // Startup diagnostics only: no vault payloads, passwords or exception data dictionaries.
            var message = exception.ToString() + "\n" + detail;
            if (exception is Microsoft.UI.Xaml.Markup.XamlParseException)
                foreach (System.Collections.DictionaryEntry item in exception.Data) message += "\n" + item.Key + ": " + item.Value;
            File.WriteAllText(Path.Combine(folder, "startup-error.log"), message);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
        MainWindow = new MainWindow();
        MainWindow.Activate();
        MainWindow.Closed += (_, _) => { instance?.ReleaseMutex(); instance?.Dispose(); instance = null; };
    }
}
