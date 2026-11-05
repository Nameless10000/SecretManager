using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public static class VaultInsights
{
    public static IReadOnlyList<string> Audit(VaultDocument document)
    {
        var secrets = document.Associations.SelectMany(topic => topic.Fields
            .Where(field => field.Kind == FieldKind.Secret && field.Value.Length > 0)
            .Select(field => (Topic: topic.Title, Field: field))).ToArray();
        var duplicates = secrets.GroupBy(item => item.Field.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).SelectMany(group => group).Select(item => item.Field.Id).ToHashSet();
        var findings = new List<string>();
        foreach (var item in secrets)
        {
            if (item.Field.Value.Length < 12)
            {
                findings.Add($"{item.Topic} / {item.Field.Description}: короткое значение (менее 12 символов)");
            }

            if (duplicates.Contains(item.Field.Id))
            {
                findings.Add($"{item.Topic} / {item.Field.Description}: повторяется в другом поле");
            }
        }

        return findings;
    }

    public static IReadOnlyList<SecretAssociation> Due(VaultDocument document, DateTimeOffset now)
    {
        return document.Associations.Where(topic => topic.ExpiresAt <= now.AddDays(7))
            .OrderBy(topic => topic.ExpiresAt).ToArray();
    }
}
