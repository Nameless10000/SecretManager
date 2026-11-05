namespace SecretManager.Core.Models;

public sealed record SecretAssociation
{
    public bool IsFavorite { get; init; }
    public List<string> Tags { get; init; } = [];
    public DateTimeOffset? ExpiresAt { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<SecretField> Fields { get; init; } = [];
}
