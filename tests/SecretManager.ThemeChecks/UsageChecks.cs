using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace SecretManager.ThemeChecks;

internal static class UsageChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var previous = Environment.GetEnvironmentVariable("SECRETMANAGER_DATA_DIR");
        var profile = Path.Combine(output, "usage-profiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var fixture = Path.Combine(output, "attachment-fixtures", "sample.png");
        if (!File.Exists(fixture))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixture)!);
            using var image = await AttachmentChecks.MakeImage(BitmapEncoder.PngEncoderId);
            using var source = image.AsStreamForRead(); using var destination = File.Create(fixture);
            await source.CopyToAsync(destination);
        }
        Environment.SetEnvironmentVariable("SECRETMANAGER_DATA_DIR", profile);
        SecretManager.App.MainWindow? window = null;
        try
        {
            window = new SecretManager.App.MainWindow { Title = "SecretManager — usage checks (synthetic data)" }; window.Activate();
            return await window.RunUsageScenarios(output, name => File.WriteAllText(Path.Combine(output, "usage-stage.txt"), name));
        }
        finally
        {
            if (window is not null) await window.CloseAfterUsageChecks();
            Environment.SetEnvironmentVariable("SECRETMANAGER_DATA_DIR", previous);
        }
    }
}
