namespace SecretManager.ThemeChecks;

public partial class App : Microsoft.UI.Xaml.Application
{
    private ThemeCheckWindow? window;
    public App()
    {
        UnhandledException += (_, e) => Report(e.Exception.ToString() + "\n" + e.Message);
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { window = new ThemeCheckWindow(); window.Activate(); }
        catch (Exception ex) { Report(ex.ToString()); Environment.ExitCode = 1; Exit(); }
    }
    private static void Report(string error)
    {
        var output = Environment.GetEnvironmentVariable("SECRETMANAGER_THEME_CHECK_OUTPUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = error }));
    }
}
