using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SecretManager.ThemeChecks;

// Only the synthetic test process installs this observer. It records a WinRT
// stowed error without handling it or changing the process's failure exit code.
internal static class NativeFailureDiagnostics
{
    private delegate int ExceptionObserver(nint exceptionPointers);
    private static readonly ExceptionObserver observer = Observe;
    private static string output = "";
    private static int writing;

    public static void Install()
    {
        output = Environment.GetEnvironmentVariable("SECRETMANAGER_THEME_CHECK_OUTPUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(output);
        AddVectoredExceptionHandler(1, observer);
    }

    private static int Observe(nint pointers)
    {
        var record = Marshal.ReadIntPtr(pointers);
        if (unchecked((uint)Marshal.ReadInt32(record)) != 0xC000027B || Interlocked.Exchange(ref writing, 1) != 0) return 0;
        try
        {
            var message = new StringBuilder("WinRT stowed exception (x64)\n");
            var entries = Marshal.ReadIntPtr(record, 32);
            var count = Math.Min(Marshal.ReadInt64(record, 40), 16);
            for (var i = 0; i < count; i++) Describe(Marshal.ReadIntPtr(entries, i * 8), message, 0);
            File.WriteAllText(Path.Combine(output, "native-error.txt"), message.ToString());
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "native-error.txt"), "Diagnostic failed: " + ex.Message); }
        finally { Volatile.Write(ref writing, 0); }
        return 0; // EXCEPTION_CONTINUE_SEARCH: never suppress the original failure.
    }

    private static void Describe(nint info, StringBuilder message, int depth)
    {
        if (info == 0 || depth > 3) return;
        var size = Marshal.ReadInt32(info);
        if (size < 40 || size > 256) return;
        var result = Marshal.ReadInt32(info, 8);
        var form = Marshal.ReadInt32(info, 12) & 3;
        message.AppendLine($"HRESULT=0x{result:X8} ({new Win32Exception(result).Message}), form={form}");
        if (form == 1 && Marshal.ReadInt32(info, 24) == 8)
        {
            var words = Math.Min(Marshal.ReadInt32(info, 28), 80);
            var stack = Marshal.ReadIntPtr(info, 32);
            for (var i = 0; i < words; i++)
            {
                var address = Marshal.ReadIntPtr(stack, i * 8);
                var name = new StringBuilder(512);
                if (GetModuleHandleEx(6, address, out var module)) GetModuleFileName(module, name, name.Capacity);
                message.AppendLine($"  {Path.GetFileName(name.ToString())}+0x{address.ToInt64() - module.ToInt64():X}");
            }
        }
        else if (form == 2) message.AppendLine(Marshal.PtrToStringUni(Marshal.ReadIntPtr(info, 16)));
        if (size >= 56 && Marshal.ReadInt32(info, 40) == 0x53544F57) Describe(Marshal.ReadIntPtr(info, 48), message, depth + 1);
    }

    [DllImport("kernel32.dll")] private static extern nint AddVectoredExceptionHandler(uint first, ExceptionObserver handler);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool GetModuleHandleEx(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(nint module, StringBuilder name, int size);
}
