namespace SecretManager.App.Services;

public sealed record HotkeyGesture(uint Key = 0x53, uint Modifiers = 3, bool Enabled = true)
{
    public bool IsValid => (Modifiers & ~15u) == 0 && (Modifiers & 11u) != 0 &&
        (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87);
}
