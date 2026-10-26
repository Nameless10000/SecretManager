using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SecretManager.App.Services;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SecretManager.UpdateTests", Guid.NewGuid().ToString("N"));
    private static string Release(string version = "0.1.5", string? url = null, string? digest = null, long size = 5,
        bool draft = false, bool prerelease = false) => JsonSerializer.Serialize(new
    {
        name = "SecretManager " + version, draft, prerelease,
        assets = new[] { new { name = "SecretManager-Setup.exe", size, digest = digest ?? "sha256:" + new string('a', 64),
            browser_download_url = url ?? "https://github.com/Nameless10000/SecretManager/releases/download/latest/SecretManager-Setup.exe" } }
    });

    [Theory]
    [InlineData("0.1.4", false)]
    [InlineData("0.1.5", false)]
    [InlineData("0.1.6", true)]
    [InlineData("0.10.0", true)]
    [InlineData("— latest", false)]
    public void VersionsAreComparedNumericallyAndNeverDowngraded(string version, bool available) =>
        Assert.Equal(available, UpdateService.ParseRelease(Release(version), new(0, 1, 5, 0)) is not null);

    [Fact]
    public void DraftAndPrereleaseAreIgnored()
    {
        Assert.Null(UpdateService.ParseRelease(Release(draft: true), new(0, 1, 4)));
        Assert.Null(UpdateService.ParseRelease(Release(prerelease: true), new(0, 1, 4)));
    }

    [Theory]
    [InlineData("http://github.com/Nameless10000/SecretManager/releases/download/latest/SecretManager-Setup.exe")]
    [InlineData("https://evil.example/SecretManager-Setup.exe")]
    [InlineData("https://github.com/Other/SecretManager/releases/download/latest/SecretManager-Setup.exe")]
    [InlineData("https://github.com/Nameless10000/SecretManager/releases/download/latest/SecretManager-Setup.exe?anything=1")]
    public void OnlyTheExactHttpsRepositoryInstallerIsAccepted(string url) =>
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseRelease(Release(url: url), new(0, 1, 4)));

    [Theory]
    [InlineData("sha1:abcdef", 5)]
    [InlineData("sha256:xyz", 5)]
    [InlineData(null, 0)]
    [InlineData(null, 268435457)]
    public void DigestAndSizeAreMandatory(string? digest, long size) =>
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseRelease(Release(digest: digest, size: size), new(0, 1, 4)));

    [Fact]
    public async Task DownloadIsBoundedAndDigestMismatchRemovesTheCandidate()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var release = UpdateService.ParseRelease(Release(), new(0, 1, 4))!;
        using var http = Client(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(http).DownloadAsync(release, folder, default));
        Assert.Empty(Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories));
        using var tooLarge = Client([1, 2, 3, 4, 5, 6], false);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(tooLarge).DownloadAsync(release, folder, default));
        using var truncated = Client([1, 2, 3], false);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(truncated).DownloadAsync(release, folder, default));
        Assert.Empty(Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task VerifiedDownloadAndCancellationLeaveNoPartialInstaller()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var release = UpdateService.ParseRelease(Release(digest: "sha256:" + Convert.ToHexString(SHA256.HashData(bytes))), new(0, 1, 4))!;
        using var http = Client(bytes);
        var candidate = await new UpdateService(http).DownloadAsync(release, folder, default);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(candidate.Installer));
        using var offline = new HttpClient(new Handler(_ => throw new HttpRequestException("Synthetic offline")));
        Assert.Equal(candidate.Installer, (await new UpdateService(offline).DownloadAsync(release, folder, default)).Installer);
        Assert.Throws<InvalidDataException>(() => UpdateService.ValidateInstaller(candidate));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateService(http).DownloadAsync(release, folder, cancelled.Token));
        Assert.Single(Directory.GetFiles(folder, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FailedHttpResponseAndOversizedMetadataCannotCreateAnInstaller()
    {
        using var unavailable = new HttpClient(new Handler(_ => new(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateService(unavailable).CheckAsync(new(0, 1, 4), default));
        using var oversized = Client(new byte[1024 * 1024 + 1], false);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(oversized).CheckAsync(new(0, 1, 4), default));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task UpdateHelperPassesQuotedDirectoryAndVerifiesDigestBeforeExecuting()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fixture = Path.Combine(AppContext.BaseDirectory, "update-fixture");
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(fixture)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var installer = Path.Combine(folder, "SecretManager.UpdateFixture.exe");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installer))).ToLowerInvariant();
        var installDirectory = Path.Combine(folder, "Папка с пробелами & $ символами"); Directory.CreateDirectory(installDirectory);
        var update = new PreparedUpdate(new(new(0, 1, 5), new("https://github.com"), new FileInfo(installer).Length, hash), installer);
        var parentInfo = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true };
        parentInfo.ArgumentList.Add("--parent"); parentInfo.ArgumentList.Add(folder);
        using var parent = Process.Start(parentInfo)!;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            while (!File.Exists(Path.Combine(folder, "parent-ready"))) await Task.Delay(20, timeout.Token);
        var info = UpdateService.CreateApplyProcess(update, parent.Id, installDirectory);
        info.RedirectStandardError = true;
        using (var helper = Process.Start(info)!)
        {
            await Task.Delay(300);
            Assert.False(File.Exists(Path.Combine(installDirectory, "fixture-invocation.json")));
            await File.WriteAllTextAsync(Path.Combine(folder, "parent-release"), "release");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await helper.WaitForExitAsync(timeout.Token);
            await parent.WaitForExitAsync(timeout.Token);
            var log = Path.Combine(folder, "update-result.txt");
            Assert.True(helper.ExitCode == 0, await helper.StandardError.ReadToEndAsync() + (File.Exists(log) ? await File.ReadAllTextAsync(log) : "No update log"));
        }
        var invocation = Path.Combine(installDirectory, "fixture-invocation.json");
        Assert.True(File.Exists(invocation), await File.ReadAllTextAsync(Path.Combine(folder, "update-result.txt")));
        var args = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(invocation))!;
        Assert.Contains("/DIR=" + installDirectory, args); Assert.Contains("/AUTORESTART=1", args);
        Assert.Contains("/NOCLOSEAPPLICATIONS", args); Assert.False(File.Exists(installer));
        File.Delete(invocation);
        File.Copy(Path.Combine(fixture, "SecretManager.UpdateFixture.exe"), installer);
        info = UpdateService.CreateApplyProcess(update with { Release = update.Release with { Sha256 = new string('0', 64) } }, parent.Id, installDirectory);
        using (var helper = Process.Start(info)!)
        { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await helper.WaitForExitAsync(timeout.Token); Assert.Equal(1, helper.ExitCode); }
        Assert.False(File.Exists(invocation)); Assert.True(File.Exists(installer));
    }

    [Fact]
    public void DevicePreferencesRoundTripAndCorruptFilesFallBackToDefaults()
    {
        Assert.True(DevicePreferences.Load(folder).AutomaticUpdates);
        new DevicePreferences(false).Save(folder); Assert.False(DevicePreferences.Load(folder).AutomaticUpdates);
        File.WriteAllText(Path.Combine(folder, "device-settings.json"), "broken");
        Assert.True(DevicePreferences.Load(folder).AutomaticUpdates);
    }

    private static HttpClient Client(byte[] bytes, bool contentLength = true) => new(new Handler(request =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request,
            Content = contentLength ? new ByteArrayContent(bytes) : new StreamContent(new NonSeekable(bytes)) };
        return response;
    }));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes) { public override bool CanSeek => false; }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
