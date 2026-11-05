namespace SecretManager.Core.Models;

public sealed record PasswordGeneratorOptions
{
    public int Length { get; init; } = 24;
    public bool Lowercase { get; init; } = true;
    public bool Uppercase { get; init; } = true;
    public bool Digits { get; init; } = true;
    public bool Symbols { get; init; } = true;
    public bool ExcludeSimilar { get; init; } = true;
}
