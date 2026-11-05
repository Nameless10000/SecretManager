namespace SecretManager.Core.Models;

public sealed record SecretField
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Description { get; init; } = "";
    public string Value { get; init; } = "";
    public FieldKind Kind { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
}
