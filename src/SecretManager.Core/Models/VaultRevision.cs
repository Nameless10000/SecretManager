namespace SecretManager.Core.Models;

public sealed record VaultRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool Deleted { get; init; }
    public SecretAssociation Association { get; init; } = new();
}
