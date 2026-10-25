using System.Runtime.InteropServices;

namespace SecretManager.App.Services;

internal static class NativeMethods
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    internal static void ActivateExistingWindow()
    {
        var hwnd = FindWindow(null, "SecretManager");
        if (hwnd != 0) { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
    }
    internal static void Show(nint hwnd) { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
}
