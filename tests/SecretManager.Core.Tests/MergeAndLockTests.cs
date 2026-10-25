using SecretManager.Core.Models;
using SecretManager.Core.Services;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class MergeAndLockTests
{
    private static SecretAssociation Topic(string title, string value) => new()
    { Title = title, Fields = [new SecretField { Description = "Ключ", Value = value }] };

    [Theory]
    [InlineData(ConflictResolution.KeepLocal, 1, "old")]
    [InlineData(ConflictResolution.UseImported, 1, "new")]
    [InlineData(ConflictResolution.KeepBoth, 2, "old")]
    public void ConflictChoicesHaveTheExpectedResult(ConflictResolution choice, int count, string firstValue)
    {
        var local = new VaultDocument { Associations = [Topic("Сайт", "old")] };
        var incoming = new VaultDocument { Associations = [Topic("сайт", "new")] };
        Assert.Single(VaultMerge.FindConflicts(local, incoming));
        var result = VaultMerge.Merge(local, incoming, new Dictionary<Guid, ConflictResolution> { [incoming.Associations[0].Id] = choice });
        Assert.Equal(count, result.Associations.Count);
        Assert.Equal(firstValue, result.Associations[0].Fields[0].Value);
        Assert.Equal("old", local.Associations[0].Fields[0].Value);
    }

    [Fact]
    public void IdenticalImportsAreIdempotent()
    {
        var local = new VaultDocument { Associations = [Topic("Сайт", "old")] };
        Assert.Empty(VaultMerge.FindConflicts(local, local));
        Assert.Single(VaultMerge.Merge(local, local, new Dictionary<Guid, ConflictResolution>()).Associations);
    }

    [Fact]
    public void UnresolvedConflictDoesNotMutateLocalData()
    {
        var local = new VaultDocument { Associations = [Topic("Сайт", "old")] };
        var incoming = new VaultDocument { Associations = [Topic("Сайт", "new")] };
        Assert.Throws<InvalidOperationException>(() => VaultMerge.Merge(local, incoming, new Dictionary<Guid, ConflictResolution>()));
        Assert.Equal("old", local.Associations[0].Fields[0].Value);
    }

    [Fact]
    public void ImportedSettingsNeverDisableLocalProtection()
    {
        var local = new VaultDocument();
        var incoming = new VaultDocument { Settings = new() { AutoLockMinutes = 0, LockWhenHidden = false } };
        Assert.Equal(5, VaultMerge.Merge(local, incoming, new Dictionary<Guid, ConflictResolution>()).Settings.AutoLockMinutes);
    }

    [Fact]
    public void FocusTimeoutUsesElapsedTimeAndResetsOnFocus()
    {
        var policy = new FocusLockPolicy(); var now = DateTimeOffset.UtcNow;
        policy.SetFocused(false, now);
        policy.SetFocused(false, now.AddMinutes(4));
        Assert.False(policy.ShouldLock(now.AddMinutes(4.99), 5));
        Assert.True(policy.ShouldLock(now.AddMinutes(5), 5));
        Assert.False(policy.ShouldLock(now.AddDays(1), 0));
        policy.SetFocused(true, now.AddMinutes(6));
        Assert.False(policy.ShouldLock(now.AddMinutes(20), 5));
    }
}
