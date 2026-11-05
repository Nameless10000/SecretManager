using System.Runtime.InteropServices;
using SecretManager.App.Services;

namespace SecretManager.ThemeChecks;

internal static class NativeHotkeyChecks
{
    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nint parameter, nint value);

    internal static void Run()
    {
        var invoked = false;
        using (var hotkey = new GlobalHotkey(0x87))
        {
            if (!hotkey.Registered)
            {
                throw new InvalidOperationException("Synthetic Ctrl+Alt+F24 registration failed.");
            }

            hotkey.Invoked += () => invoked = true;
            SendMessageW(hotkey.Handle, 0x0312, 1, 0);
            if (!invoked)
            {
                throw new InvalidOperationException("WM_HOTKEY did not invoke the registered callback.");
            }
        }

        using var repeated = new GlobalHotkey(0x87);
        if (!repeated.Registered)
        {
            throw new InvalidOperationException("Disposed hotkey retained its native registration.");
        }
    }
}
