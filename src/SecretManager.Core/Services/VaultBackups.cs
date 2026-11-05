using SecretManager.Core.Security;

namespace SecretManager.Core.Services;

public sealed class VaultBackups(string databasePath)
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "Backups");

    public void Write(EncryptedEnvelope envelope, bool force = false)
    {
        Directory.CreateDirectory(DirectoryPath);
        var existing = Directory.GetFiles(DirectoryPath, "*.smbackup").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        if (!force && existing.Length > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(existing[0]) < TimeSpan.FromMinutes(30))
        {
            return;
        }

        var path = Path.Combine(DirectoryPath, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.smbackup");
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, ExportPackage.Create(envelope));
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        foreach (var old in existing.Skip(9))
        {
            File.Delete(old);
        }
    }
}
