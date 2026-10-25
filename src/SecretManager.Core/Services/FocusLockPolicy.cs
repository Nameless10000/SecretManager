namespace SecretManager.Core.Services;

public sealed class FocusLockPolicy
{
    private DateTimeOffset? unfocusedSince;
    public void SetFocused(bool focused, DateTimeOffset now)
    {
        if (focused) unfocusedSince = null;
        else unfocusedSince ??= now;
    }
    public bool ShouldLock(DateTimeOffset now, int timeoutMinutes) => timeoutMinutes > 0 &&
        unfocusedSince is { } since && now - since >= TimeSpan.FromMinutes(timeoutMinutes);
}
