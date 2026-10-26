using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace SecretManager.App.Services;

internal sealed record UpdateRelease(Version Version, Uri Download, long Size, string Sha256);
internal sealed record PreparedUpdate(UpdateRelease Release, string Installer);
internal enum UpdateCheckStatus { UpToDate, Available, LegacyRelease, NotPublished }
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateRelease? Release = null);

internal sealed class UpdateService(HttpClient client)
{
    internal const string Repository = "Nameless10000/SecretManager";
    internal const int MaximumInstallerBytes = 256 * 1024 * 1024;
    internal static readonly Uri ManifestUri = new($"https://github.com/{Repository}/releases/download/latest/SecretManager-Setup.sha256");
    internal static readonly Uri InstallerUri = new($"https://github.com/{Repository}/releases/download/latest/SecretManager-Setup.exe");

    internal static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SecretManager/" + CurrentVersion);
        return http;
    }
    internal static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new(0, 0, 0);
    internal static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    internal async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateCheckStatus.NotPublished);
        response.EnsureSuccessStatusCode();
        ValidateDownloadResponse(response);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = await ReadBoundedAsync(stream, 4096, cancellationToken);
        return ParseManifest(System.Text.Encoding.UTF8.GetString(bytes), current);
    }

    internal static UpdateCheckResult ParseManifest(string manifest, Version current)
    {
        var lines = manifest.TrimStart('\uFEFF').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !Regex.IsMatch(lines[0], @"\A[0-9a-fA-F]{64}  SecretManager-Setup\.exe\z"))
            throw new InvalidDataException("Некорректный SHA-256 установщика.");
        // The old checksum file is valid, but cannot tell us a release version.
        // Do not fall back to the rate-limited API or claim that this means up to date.
        if (lines.Length == 1) return new(UpdateCheckStatus.LegacyRelease);
        if (lines.Length != 3) throw new InvalidDataException("Некорректные метаданные обновления.");
        var match = Regex.Match(lines[1], @"\A# SecretManager-Version: (\d+\.\d+\.\d+)\z", RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version))
            throw new InvalidDataException("Некорректная версия обновления.");
        var sizeMatch = Regex.Match(lines[2], @"\A# SecretManager-Size: (\d+)\z", RegexOptions.CultureInvariant);
        if (!sizeMatch.Success || !long.TryParse(sizeMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ||
            size is <= 0 or > MaximumInstallerBytes)
            throw new InvalidDataException("Некорректный размер установщика.");
        return Normalize(version) <= Normalize(current) ? new(UpdateCheckStatus.UpToDate) :
            new(UpdateCheckStatus.Available, new(version, InstallerUri, size, lines[0][..64].ToLowerInvariant()));
    }

    internal static string DescribeHttpFailure(HttpRequestException error) => error.StatusCode switch
    {
        HttpStatusCode.Forbidden => "GitHub отклонил запрос (HTTP 403). Возможны ограничения доступа или временный лимит. Повторите позже.",
        HttpStatusCode.TooManyRequests => "GitHub временно ограничил запросы (HTTP 429). Повторите позже.",
        HttpStatusCode.NotFound => "Файл обновления не найден в релизе (HTTP 404). Повторите проверку позже.",
        { } status when (int)status >= 500 => $"GitHub временно недоступен (HTTP {(int)status}). Повторите позже.",
        { } status => $"Не удалось загрузить обновление (HTTP {(int)status}). Проверьте доступ к GitHub.",
        null => "Не удалось подключиться к GitHub. Проверьте сеть и повторите позже."
    };

    private static void ValidateDownloadResponse(HttpResponseMessage response)
    {
        var final = response.RequestMessage?.RequestUri;
        if (final is null || final.Scheme != "https" || final.UserInfo.Length != 0 || final.Port != 443 ||
            !new[] { "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(final.Host))
            throw new InvalidDataException("Недопустимое перенаправление загрузки.");
    }

    internal async Task<PreparedUpdate> DownloadAsync(UpdateRelease release, string folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(folder, "Updates", release.Version + "-" + release.Sha256); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "SecretManager-Setup.exe");
        if (File.Exists(path))
        {
            bool valid;
            using (var cached = File.OpenRead(path)) valid = cached.Length == release.Size &&
                CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(cached, cancellationToken), Convert.FromHexString(release.Sha256));
            if (valid) return new(release, path);
            File.Delete(path);
        }
        try
        {
            using var response = await client.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            ValidateDownloadResponse(response);
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("Размер установщика не совпал с релизом.");
            using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920]; long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, release.Size - total + 1)), cancellationToken);
                    if (read == 0) break;
                    total += read; if (total > release.Size || total > MaximumInstallerBytes) throw new InvalidDataException("Установщик превышает заявленный размер.");
                    hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("Проверка SHA-256 установщика не пройдена.");
            }
            return new(release, path);
        }
        catch
        {
            // Only a file created by this operation; never touch the vault or arbitrary directories.
            File.Delete(path); if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximum - (int)output.Length + 1)), cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximum) throw new InvalidDataException("Ответ сервера слишком велик.");
            output.Write(buffer, 0, read);
        }
    }

    internal static void ValidateInstaller(PreparedUpdate update)
    {
        var actual = FileVersionInfo.GetVersionInfo(update.Installer);
        if (new Version(actual.FileMajorPart, actual.FileMinorPart, actual.FileBuildPart) != Normalize(update.Release.Version))
            throw new InvalidDataException("Версия установщика не совпала с релизом.");
    }

    internal static void LaunchInstallerAfterExit(PreparedUpdate update)
    {
        using var process = Process.Start(CreateApplyProcess(update, Environment.ProcessId, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))
            ?? throw new IOException("Не удалось запустить обновление.");
    }

    internal static ProcessStartInfo CreateApplyProcess(PreparedUpdate update, int parentProcessId, string installDirectory)
    {
        ValidateInstaller(update);
        var script = Path.Combine(Path.GetDirectoryName(update.Installer)!, "apply-update.ps1");
        File.WriteAllText(script, ApplyScript);
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), update.Installer,
            update.Release.Sha256, update.Release.Version.ToString(), installDirectory }) info.ArgumentList.Add(arg);
        return info;
    }

    // No command is constructed from a release body or a vault value. Parameters use ArgumentList.
    internal const string ApplyScript = """
        param([int]$ParentProcessId, [string]$Installer, [string]$Sha256, [string]$Version, [string]$InstallDirectory)
        $ErrorActionPreference = 'Stop'
        $logPath = [IO.Path]::Combine([IO.Path]::GetDirectoryName($Installer), 'update-result.txt')
        try {
            $parentProcess = $null
            try { $parentProcess = [Diagnostics.Process]::GetProcessById($ParentProcessId) } catch [ArgumentException] { }
            if ($parentProcess -and -not $parentProcess.WaitForExit(60000)) { throw 'ParentTimeout' }
            $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
            $installerStream = [IO.File]::OpenRead($Installer)
            try { $digest = [BitConverter]::ToString($hashAlgorithm.ComputeHash($installerStream)).Replace('-', '') }
            finally { $installerStream.Dispose(); $hashAlgorithm.Dispose() }
            if ($digest -ne $Sha256) { throw 'DigestMismatch' }
            $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($Installer)
            if (([version]::new($actual.FileMajorPart, $actual.FileMinorPart, $actual.FileBuildPart)).ToString() -ne $Version) { throw 'VersionMismatch' }
            if ($InstallDirectory.Contains('"')) { throw 'InvalidDirectory' }
            $setupInfo = [Diagnostics.ProcessStartInfo]::new()
            $setupInfo.FileName = $Installer
            $setupInfo.Arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /AUTORESTART=1 /DIR="' + $InstallDirectory + '"'
            $setupInfo.UseShellExecute = $false; $setupInfo.CreateNoWindow = $true
            $setupInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $setupProcess = [Diagnostics.Process]::Start($setupInfo); $setupProcess.WaitForExit()
            [IO.File]::WriteAllText($logPath, ('InstallerExitCode=' + $setupProcess.ExitCode))
            if ($setupProcess.ExitCode -eq 0) { [IO.File]::Delete($Installer) }
        } catch { [IO.File]::WriteAllText($logPath, ('UpdateFailed:' + $_.Exception.GetType().Name + ';Line=' + $_.InvocationInfo.ScriptLineNumber)); exit 1 }
        """;
}
