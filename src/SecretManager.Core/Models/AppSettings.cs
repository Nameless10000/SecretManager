namespace SecretManager.Core.Models;

public sealed record AppSettings
{
    public PasswordGeneratorOptions PasswordGenerator { get; init; } = new();
    public int MaxStorageMegabytes { get; init; } = 64;
    public bool AutomaticBackups { get; init; } = true;
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
