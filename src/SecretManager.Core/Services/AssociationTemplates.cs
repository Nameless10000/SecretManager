using SecretManager.Core.Models;

namespace SecretManager.Core.Services;

public static class AssociationTemplates
{
    public static IReadOnlyList<string> Names { get; } = ["Пустая", "Сайт", "Сервер", "API", "Сертификат"];

    public static SecretAssociation Create(string title, int template)
    {
        var descriptions = template switch
        {
            0 => Array.Empty<string>(),
            1 => ["Адрес сайта", "Логин", "Пароль"],
            2 => ["Адрес сервера", "Порт", "Пользователь", "Пароль"],
            3 => ["Адрес API", "Токен"],
            4 => ["Владелец", "Примечание"],
            _ => throw new ArgumentException("Неизвестный шаблон.")
        };

        return new SecretAssociation
        {
            Title = title,
            Fields = descriptions.Select(description => new SecretField
            {
                Description = description,
                Kind = description is "Пароль" or "Токен" ? FieldKind.Secret : FieldKind.Text
            }).ToList()
        };
    }
}
