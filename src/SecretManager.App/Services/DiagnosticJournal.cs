using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SecretManager.App.Services;

internal enum AppEvent
{
    Started, PreviousRunInterrupted, ExitRequested, Stopped, StartupFailed, UnhandledException, UnobservedTask,
    ActionStarted, ActionCompleted, ActionCancelled, ActionRejected, ActionFailed,
    Locked, Unlocked, SettingsOpened, SettingsClosed, AppearanceApplying, AppearanceApplied, WindowHidden, WindowShown
}

// Deliberately accepts no payloads, exception messages, data dictionaries or user
// strings. Method names come from CallerMemberName; error stacks omit file paths.
internal sealed class DiagnosticJournal(string dataDirectory, int maximumBytes = 256 * 1024)
{
    private readonly object sync = new();
    private bool fatal;
    internal string DirectoryPath { get; } = Path.Combine(dataDirectory, "Logs");
    internal string LogPath => Path.Combine(DirectoryPath, "application.log");
    internal string PendingPath => Path.Combine(DirectoryPath, "run.pending");
    private readonly string version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    internal void Start()
    {
        try
        {
            lock (sync)
            {
                if (File.Exists(PendingPath)) Record(AppEvent.PreviousRunInterrupted);
                Record(AppEvent.Started);
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(PendingPath, DateTimeOffset.UtcNow.ToString("O"));
            }
        }
        catch (Exception ex) when (IsFileError(ex)) { }
    }
    internal void StopCleanly()
    {
        try
        {
            lock (sync)
            {
                if (fatal) return;
                Record(AppEvent.Stopped);
                File.Delete(PendingPath);
            }
        }
        catch (Exception ex) when (IsFileError(ex)) { }
    }
    internal void Record(AppEvent kind, Exception? error = null, [CallerMemberName] string operation = "")
    {
        try
        {
            lock (sync)
            {
                if (kind is AppEvent.UnhandledException or AppEvent.StartupFailed or AppEvent.ActionFailed) fatal = true;
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= maximumBytes)
                    File.Move(LogPath, Path.Combine(DirectoryPath, "application.previous.log"), true);
                var entry = new
                {
                    Time = DateTimeOffset.UtcNow, Version = version, Runtime = Environment.Version.ToString(),
                    Event = kind.ToString(), Operation = operation, Error = Describe(error, 0)
                };
                File.AppendAllText(LogPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (IsFileError(ex)) { /* Diagnostics must never crash the application. */ }
    }
    private static object? Describe(Exception? error, int depth)
    {
        if (error is null || depth > 3) return null;
        return new
        {
            Type = error.GetType().FullName, HResult = $"0x{error.HResult:X8}",
            HttpStatus = error is System.Net.Http.HttpRequestException http ? (int?)http.StatusCode : null,
            HttpError = error is System.Net.Http.HttpRequestException network ? network.HttpRequestError.ToString() : null,
            Frames = (new StackTrace(error, false).GetFrames() ?? []).Take(24).Select(f =>
            {
                var method = f.GetMethod(); return method?.DeclaringType?.FullName + "." + method?.Name;
            }).ToArray(),
            Inner = Describe(error.InnerException, depth + 1)
        };
    }
    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;
}

internal static class AppDiagnostics
{
    internal static DiagnosticJournal Journal { get; } = new(AppPaths.DataDirectory);
}
