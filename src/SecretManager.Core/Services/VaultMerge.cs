using System.Text.Json;
using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public enum ConflictResolution { KeepLocal, UseImported, KeepBoth }
public sealed record ImportConflict(SecretAssociation Local, SecretAssociation Imported);

public static class VaultMerge
{
    public static IReadOnlyList<ImportConflict> FindConflicts(VaultDocument local, VaultDocument imported)
    {
        VaultValidation.Validate(imported);
        return imported.Associations.Select(incoming => new
        {
            Incoming = incoming,
            Existing = FindMatch(local.Associations, incoming)
        }).Where(x => x.Existing is not null && !Equivalent(x.Existing, x.Incoming))
            .Select(x => new ImportConflict(x.Existing!, x.Incoming)).ToArray();
    }

    public static VaultDocument Merge(VaultDocument local, VaultDocument imported,
        IReadOnlyDictionary<Guid, ConflictResolution> resolutions)
    {
        VaultValidation.Validate(local);
        VaultValidation.Validate(imported);
        var result = local.Associations.ToList();
        foreach (var incoming in imported.Associations)
        {
            var existing = FindMatch(result, incoming);
            if (existing is null) { result.Add(ReidentifyFieldCollisions(incoming, result)); continue; }
            if (Equivalent(existing, incoming)) continue;
            if (!resolutions.TryGetValue(incoming.Id, out var choice))
                throw new InvalidOperationException("Нужно разрешить все конфликты импорта.");
            switch (choice)
            {
                case ConflictResolution.KeepLocal: break;
                case ConflictResolution.UseImported:
                    result.Remove(existing);
                    result.Add(ReidentifyFieldCollisions(incoming with { Id = existing.Id }, result));
                    break;
                case ConflictResolution.KeepBoth:
                    var baseTitle = incoming.Title[..Math.Min(incoming.Title.Length, 170)];
                    var duplicateTitle = baseTitle + " (импорт)";
                    var suffix = 2;
                    while (result.Any(a => string.Equals(a.Title, duplicateTitle, StringComparison.OrdinalIgnoreCase)))
                        duplicateTitle = baseTitle + $" (импорт {suffix++})";
                    var duplicate = incoming with
                    {
                        Id = Guid.NewGuid(), Title = duplicateTitle,
                        Fields = incoming.Fields.Select(f => f with { Id = Guid.NewGuid() }).ToList()
                    };
                    result.Add(duplicate);
                    break;
                default: throw new InvalidDataException("Неизвестное решение конфликта.");
            }
        }
        var document = local with { Associations = result };
        VaultValidation.Validate(document);
        return document;
    }

    private static SecretAssociation? FindMatch(IEnumerable<SecretAssociation> values, SecretAssociation incoming) =>
        values.FirstOrDefault(a => a.Id == incoming.Id) ??
        values.FirstOrDefault(a => string.Equals(a.Title.Trim(), incoming.Title.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool Equivalent(SecretAssociation a, SecretAssociation b) => a.Title == b.Title &&
        JsonSerializer.Serialize(a.Fields, VaultValidation.JsonOptions) == JsonSerializer.Serialize(b.Fields, VaultValidation.JsonOptions);

    private static SecretAssociation ReidentifyFieldCollisions(SecretAssociation incoming, IEnumerable<SecretAssociation> existing)
    {
        var used = existing.Select(a => a.Id).Concat(existing.SelectMany(a => a.Fields.Select(f => f.Id))).ToHashSet();
        var topicId = used.Contains(incoming.Id) ? Guid.NewGuid() : incoming.Id;
        return incoming with { Id = topicId, Fields = incoming.Fields.Select(f => used.Contains(f.Id) || f.Id == topicId
            ? f with { Id = Guid.NewGuid() } : f).ToList() };
    }
}
