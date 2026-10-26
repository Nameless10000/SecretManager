namespace SecretManager.Core.Models;

public enum FieldKind { Text, Secret, Image, File }
public enum AppTheme { System, Light, Dark, Midnight, Forest, Custom }
public enum AnimationLevel { Off, Medium, Maximum }

public sealed record SecretField
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Description { get; init; } = "";
    public string Value { get; init; } = "";
    public FieldKind Kind { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
}

public sealed record SecretAssociation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<SecretField> Fields { get; init; } = [];
}

public sealed record AppSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;
    public int AutoLockMinutes { get; init; } = 5;
    public int ClipboardClearSeconds { get; init; } = 30;
    public bool CloseToTray { get; init; } = true;
    public bool LockWhenHidden { get; init; } = true;
    public AnimationLevel AnimationLevel { get; init; } = AnimationLevel.Maximum;
    public double MotionSpeed { get; init; } = 1;
    public string AccentColor { get; init; } = "#5978FF";
    public string BackgroundColor { get; init; } = "#171B27";
    public string FontFamily { get; init; } = "Segoe UI Variable";
    public double FontSize { get; init; } = 14;
    public double CornerRadius { get; init; } = 14;
    public bool CompactLayout { get; init; }
    public bool SingleExpandedAssociation { get; init; } = true;
}

public sealed record VaultDocument
{
    public int SchemaVersion { get; init; } = 1;
    // A selected transfer is a patch: omitted fields must survive import.
    public bool IsPartial { get; init; }
    public List<SecretAssociation> Associations { get; init; } = [];
    public AppSettings Settings { get; init; } = new();
}
