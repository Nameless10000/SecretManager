using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public static class VaultSelection
{
    public static VaultDocument Select(VaultDocument source, IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> selection)
    {
        VaultValidation.Validate(source);
        if (selection.Count == 0 && source.Associations.Count != 0)
            throw new ArgumentException("Выберите хотя бы одну ассоциацию.");
        var known = source.Associations.ToDictionary(a => a.Id);
        foreach (var (id, fields) in selection)
        {
            if (!known.TryGetValue(id, out var association) || fields is null ||
                fields.Any(field => !association.Fields.Any(f => f.Id == field)))
                throw new ArgumentException("Выбор содержит неизвестные ассоциации или поля.");
        }
        var associations = source.Associations.Where(a => selection.ContainsKey(a.Id))
            .Select(a => a with { Fields = a.Fields.Where(f => selection[a.Id].Contains(f.Id)).ToList() }).ToList();
        var partial = source.IsPartial || associations.Count != source.Associations.Count ||
            associations.Any(a => a.Fields.Count != known[a.Id].Fields.Count);
        return source with
        {
            Associations = associations,
            IsPartial = partial,
            // Older clients reject v2 instead of silently replacing a whole topic with a partial one.
            SchemaVersion = partial ? 2 : 1
        };
    }
}
