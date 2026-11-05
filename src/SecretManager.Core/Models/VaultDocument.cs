namespace SecretManager.Core.Models;

public enum FieldKind { Text, Secret, Image, File }
public enum AppTheme { System, Light, Dark, Midnight, Forest, Custom }
public enum AnimationLevel { Off, Medium, Maximum }




public sealed record VaultDocument
{
    public List<VaultRevision> Revisions { get; init; } = [];
    public int SchemaVersion { get; init; } = 1;
    // A selected transfer is a patch: omitted fields must survive import.
    public bool IsPartial { get; init; }
    public List<SecretAssociation> Associations { get; init; } = [];
    public AppSettings Settings { get; init; } = new();
}
