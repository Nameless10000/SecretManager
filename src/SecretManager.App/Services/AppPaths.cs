namespace SecretManager.App.Services;

internal static class AppPaths
{
    internal static string DataDirectory => Environment.GetEnvironmentVariable("SECRETMANAGER_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretManager");
}
