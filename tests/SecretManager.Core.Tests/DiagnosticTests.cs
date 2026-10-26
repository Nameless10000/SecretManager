using SecretManager.App.Services;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class DiagnosticTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SecretManager.Diagnostics.Tests", Guid.NewGuid().ToString());
    [Fact]
    public void LogsContainErrorCodesAndOperationsWithoutMessagesOrExceptionData()
    {
        var journal = new DiagnosticJournal(folder);
        var error = new InvalidOperationException("private password, title and filename", new IOException("private path"));
        error.Data["secret"] = "private attachment value";
        journal.Record(AppEvent.ActionRejected, error);
        var text = File.ReadAllText(journal.LogPath);
        Assert.Contains(nameof(LogsContainErrorCodesAndOperationsWithoutMessagesOrExceptionData), text);
        Assert.Contains(nameof(InvalidOperationException), text); Assert.Contains("HResult", text);
        Assert.DoesNotContain("private", text); Assert.DoesNotContain("attachment", text);
    }
    [Fact]
    public void HttpStatusIsRecordedWithoutUrlsHeadersOrResponseText()
    {
        var journal = new DiagnosticJournal(folder);
        var error = new HttpRequestException("private URL and response", null, System.Net.HttpStatusCode.Forbidden);
        error.Data["Authorization"] = "private token";
        journal.Record(AppEvent.ActionRejected, error, "CheckUpdates");
        var text = File.ReadAllText(journal.LogPath);
        Assert.Contains("\"HttpStatus\":403", text); Assert.Contains("HttpError", text);
        Assert.DoesNotContain("private", text); Assert.DoesNotContain("Authorization", text);
    }
    [Fact]
    public void InterruptedRunIsReportedAndCleanShutdownRemovesTheMarker()
    {
        var first = new DiagnosticJournal(folder); first.Start();
        Assert.True(File.Exists(first.PendingPath));
        var restarted = new DiagnosticJournal(folder); restarted.Start();
        Assert.Contains(nameof(AppEvent.PreviousRunInterrupted), File.ReadAllText(first.LogPath));
        restarted.StopCleanly(); Assert.False(File.Exists(first.PendingPath));
    }
    [Fact]
    public void FatalErrorKeepsTheMarkerEvenIfProcessExitRuns()
    {
        var journal = new DiagnosticJournal(folder); journal.Start();
        journal.Record(AppEvent.UnhandledException, new InvalidOperationException()); journal.StopCleanly();
        Assert.True(File.Exists(journal.PendingPath));
    }
    [Fact]
    public void RotationKeepsOnlyCurrentAndOnePreviousLog()
    {
        var journal = new DiagnosticJournal(folder, 512);
        for (var i = 0; i < 100; i++) journal.Record(AppEvent.ActionStarted);
        Assert.Equal(2, Directory.GetFiles(journal.DirectoryPath, "*.log").Length);
        Assert.True(new FileInfo(journal.LogPath).Length < 1024);
    }
    [Fact]
    public void UnwritableDiagnosticLocationDoesNotBreakStartupOrErrorHandling()
    {
        Directory.CreateDirectory(folder); var occupied = Path.Combine(folder, "file"); File.WriteAllText(occupied, "fixture");
        var journal = new DiagnosticJournal(occupied);
        journal.Start(); journal.Record(AppEvent.UnhandledException, new IOException()); journal.StopCleanly();
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
