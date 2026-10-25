using System.Text.Json;

namespace SecretManager.Core.Models;

public static class VaultValidation
{
    public const int MaxAttachmentBytes = 10 * 1024 * 1024;
    public const int MaxDocumentBytes = 64 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false };

    public static void Validate(VaultDocument document)
    {
        if (document.SchemaVersion != 1 || document.Associations is null || document.Settings is null)
            throw new InvalidDataException("Неподдерживаемый формат хранилища.");
        if (document.Associations.Count > 10_000 || document.Associations.Any(a => a is null))
            throw new InvalidDataException("Слишком много ассоциаций или неверный формат.");
        var ids = new HashSet<Guid>();
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var association in document.Associations)
        {
            if (association.Id == Guid.Empty || !ids.Add(association.Id) ||
                string.IsNullOrWhiteSpace(association.Title) || association.Title.Length > 200 || !titles.Add(association.Title.Trim()) ||
                association.Fields is null || association.Fields.Count > 1000)
                throw new InvalidDataException("Некорректная ассоциация: проверьте название и поля.");
            foreach (var field in association.Fields)
            {
                if (field is null || field.Id == Guid.Empty || !ids.Add(field.Id) ||
                    string.IsNullOrWhiteSpace(field.Description) || field.Description.Length > 200 ||
                    field.Value is null || !Enum.IsDefined(field.Kind))
                    throw new InvalidDataException("Некорректное поле ассоциации.");
                if (field.Kind is FieldKind.File or FieldKind.Image)
                {
                    if (string.IsNullOrWhiteSpace(field.FileName) || field.FileName.Length > 255 ||
                        field.FileName.Contains('/') || field.FileName.Contains('\\') ||
                        field.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        throw new InvalidDataException("Некорректное имя вложения.");
                    if (field.Value.Length > (MaxAttachmentBytes + 2L) / 3 * 4)
                        throw new InvalidDataException("Размер вложения превышает 10 МБ.");
                    try
                    {
                        var bytes = Convert.FromBase64String(field.Value);
                        if (bytes.Length > MaxAttachmentBytes) throw new InvalidDataException("Вложение больше 10 МБ.");
                        if (field.Kind == FieldKind.Image && !IsSupportedImage(bytes))
                            throw new InvalidDataException("Предпросмотр поддерживает PNG и JPEG.");
                    }
                    catch (FormatException) { throw new InvalidDataException("Вложение содержит неверный base64."); }
                }
                else if (field.Value.Length > 1_000_000)
                    throw new InvalidDataException("Текстовое поле слишком велико.");
            }
        }
        if (!Enum.IsDefined(document.Settings.Theme) || document.Settings.AutoLockMinutes is < 0 or > 120 ||
            document.Settings.ClipboardClearSeconds is < 0 or > 300 ||
            !Enum.IsDefined(document.Settings.AnimationLevel) ||
            !double.IsFinite(document.Settings.MotionSpeed) || document.Settings.MotionSpeed is < 0.5 or > 2 ||
            !double.IsFinite(document.Settings.FontSize) || document.Settings.FontSize is < 12 or > 20 ||
            !double.IsFinite(document.Settings.CornerRadius) || document.Settings.CornerRadius is < 0 or > 28 ||
            !System.Text.RegularExpressions.Regex.IsMatch(document.Settings.AccentColor ?? "", "^#[0-9A-Fa-f]{6}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(document.Settings.BackgroundColor ?? "", "^#[0-9A-Fa-f]{6}$") ||
            !new[] { "Segoe UI Variable", "Segoe UI", "Arial", "Calibri", "Consolas" }.Contains(document.Settings.FontFamily))
            throw new InvalidDataException("Некорректные настройки.");
    }

    public static bool IsSupportedImage(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
        (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff);
}
