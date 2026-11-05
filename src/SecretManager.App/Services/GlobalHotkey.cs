using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace SecretManager.App.Services;

public sealed class GlobalHotkey : Forms.NativeWindow, IDisposable
{
    public event Action? Invoked;
    public bool Registered { get; }

    public GlobalHotkey(uint key = 0x53, uint modifiers = 3)
    {
        CreateHandle(new Forms.CreateParams());
        Registered = RegisterHotKey(Handle, 1, 0x4000 | modifiers, key);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);

    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == 0x0312 && message.WParam == 1)
        {
            Invoked?.Invoke();
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Registered)
        {
            UnregisterHotKey(Handle, 1);
        }

        DestroyHandle();
    }
}
