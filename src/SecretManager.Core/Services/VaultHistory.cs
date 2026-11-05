using System.Text.Json;
using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public static class VaultHistory
{
    public static VaultDocument Capture(VaultDocument previous, VaultDocument next, out bool requiresBackup)
    {
        requiresBackup = false;
        var revisions = next.Revisions.ToList();
        foreach (var topic in previous.Associations)
        {
            var replacement = next.Associations.FirstOrDefault(item => item.Id == topic.Id);
            if (replacement is null || JsonSerializer.Serialize(topic) != JsonSerializer.Serialize(replacement))
            {
                revisions.Add(new VaultRevision { Association = topic, Deleted = replacement is null });
            }
        }

        revisions = revisions.Where(item => item.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-30))
            .OrderByDescending(item => item.CreatedAt).Take(100).ToList();
        while (JsonSerializer.SerializeToUtf8Bytes(revisions).Length > 16 * 1024 * 1024 && revisions.Count > 0)
        {
            requiresBackup = true;
            revisions.RemoveAt(revisions.Count - 1);
        }

        return next with { SchemaVersion = 4, Revisions = revisions };
    }

    public static VaultDocument Restore(VaultDocument document, Guid revisionId)
    {
        var revision = document.Revisions.Single(item => item.Id == revisionId);
        if (document.Associations.Any(item => item.Id != revision.Association.Id &&
            string.Equals(item.Title.Trim(), revision.Association.Title.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Название уже занято. Переименуйте текущую ассоциацию перед восстановлением.");
        }

        return document with
        {
            Associations = document.Associations.Where(item => item.Id != revision.Association.Id)
                .Append(revision.Association).ToList()
        };
    }
}
