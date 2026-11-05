using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public static class VaultMigration
{
    public static VaultDocument Upgrade(VaultDocument source)
    {
        if (source.SchemaVersion >= 4)
        {
            return source;
        }

        return source with { SchemaVersion = 4,
            Associations = source.Associations.Select(UpgradeAssociation).ToList(),
            Revisions = source.Revisions.Select(revision => revision with { Association = UpgradeAssociation(revision.Association) }).ToList() };
    }

    private static SecretAssociation UpgradeAssociation(SecretAssociation source)
    {
        return source with { ExpiresAt = null,
            Fields = source.Fields.Select(field => field with { ExpiresAt = field.ExpiresAt ?? source.ExpiresAt }).ToList() };
    }
}
