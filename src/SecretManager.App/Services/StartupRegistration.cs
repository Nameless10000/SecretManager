using Microsoft.Win32;

namespace SecretManager.App.Services;

internal sealed class StartupRegistration(string executable, string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    internal string Command
    {
        get
        {
            if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || executable.Length + 14 > 260)
                throw new InvalidOperationException("Путь приложения не подходит для автозапуска Windows.");
            return $"\"{executable}\" --startup";
        }
    }
    internal bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(keyPath); return string.Equals(key?.GetValue("SecretManager") as string, Command, StringComparison.OrdinalIgnoreCase); }
    }
    internal void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
        if (enabled) key.SetValue("SecretManager", Command, RegistryValueKind.String);
        else key.DeleteValue("SecretManager", false);
    }
}
