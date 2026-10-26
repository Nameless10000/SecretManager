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
    private static string Manifest(string version = "0.1.5", string? digest = null, long size = 5) =>
        $"{digest ?? new string('a', 64)}  SecretManager-Setup.exe\n# SecretManager-Version: {version}\n# SecretManager-Size: {size}\n";

    [Theory]
    [InlineData("0.1.4", false)]
    [InlineData("0.1.5", false)]
    [InlineData("0.1.6", true)]
    [InlineData("0.10.0", true)]
    public void VersionsAreComparedNumericallyAndNeverDowngraded(string version, bool available) =>
        Assert.Equal(available, UpdateService.ParseManifest(Manifest(version), new(0, 1, 5, 0)).Status == UpdateCheckStatus.Available);

    [Fact]
    public void PartialOrMalformedMetadataCannotOfferAnUpdate()
    {
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseManifest(Manifest("0.1.5-preview"), new(0, 1, 4)));
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseManifest(Manifest() + "extra line", new(0, 1, 4)));
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseManifest(Manifest().Replace("# SecretManager-Size: 5\n", ""), new(0, 1, 4)));
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseManifest("", new(0, 1, 4)));
    }

    [Theory]
    [InlineData("http://github.com/Nameless10000/SecretManager/releases/download/latest/SecretManager-Setup.exe")]
    [InlineData("https://evil.example/SecretManager-Setup.exe")]
    [InlineData("https://github.com:8443/anything")]
    [InlineData("https://any@github.com/anything")]
    public async Task UnsafeManifestRedirectsAreRejected(string url)
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { RequestMessage = new(HttpMethod.Get, url), Content = new StringContent(Manifest()) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(http).CheckAsync(new(0, 1, 4), default));
    }

    [Theory]
    [InlineData("sha1:abcdef", 5)]
    [InlineData("sha256:xyz", 5)]
    [InlineData(null, 0)]
    [InlineData(null, 268435457)]
    public void DigestAndSizeAreMandatory(string? digest, long size) =>
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseManifest(Manifest(digest: digest, size: size), new(0, 1, 4)));

    [Fact]
    public async Task DownloadIsBoundedAndDigestMismatchRemovesTheCandidate()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var release = UpdateService.ParseManifest(Manifest(), new(0, 1, 4)).Release!;
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
        var release = UpdateService.ParseManifest(Manifest(digest: Convert.ToHexString(SHA256.HashData(bytes))), new(0, 1, 4)).Release!;
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
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateService(unavailable).CheckAsync(new(0, 1, 4), default));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        using var oversized = Client(new byte[4097], false);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateService(oversized).CheckAsync(new(0, 1, 4), default));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task ChecksUseDirectReleaseFilesWithoutAnyApiRequest()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            Assert.Equal(UpdateService.ManifestUri, request.RequestUri);
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(Manifest()) };
        }));
        var result = await new UpdateService(http).CheckAsync(new(0, 1, 4), default);
        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(UpdateService.InstallerUri, result.Release!.Download);
        Assert.Single(requests);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task LegacyReleaseDoesNotMasqueradeAsUpToDateOrFallBackToTheApi()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++; Assert.Equal(UpdateService.ManifestUri, request.RequestUri);
            return new(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(new string('a', 64) + "  SecretManager-Setup.exe\n") };
        }));
        var result = await new UpdateService(http).CheckAsync(new(0, 1, 4), default);
        Assert.Equal(UpdateCheckStatus.LegacyRelease, result.Status); Assert.Null(result.Release); Assert.Equal(1, requests);
    }

    [Fact]
    public async Task UnpublishedReleaseIsReportedWithoutAnException()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        var result = await new UpdateService(http).CheckAsync(new(0, 1, 4), default);
        Assert.Equal(UpdateCheckStatus.NotPublished, result.Status); Assert.Null(result.Release);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task HttpFailuresKeepTheStatusButNeverShowTheResponseOrExceptionMessage(HttpStatusCode status)
    {
        using var http = new HttpClient(new Handler(_ => new(status) { Content = new StringContent("private response") }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new UpdateService(http).CheckAsync(new(0, 1, 4), default));
        Assert.Equal(status, error.StatusCode);
        var message = UpdateService.DescribeHttpFailure(new("private message", null, status));
        Assert.Contains(((int)status).ToString(), message); Assert.DoesNotContain("private", message);
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
