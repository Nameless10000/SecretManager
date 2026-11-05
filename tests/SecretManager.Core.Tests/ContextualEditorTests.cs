using SecretManager.App.Services;
using SecretManager.Core.Models;
using SecretManager.Core.Security;
using SecretManager.Core.Services;
using SecretManager.Core.Storage;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class ContextualEditorTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SecretManager.Contextual", Guid.NewGuid().ToString("N"));
    private const string Password = "synthetic password for migration";

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public void GeneratorUsesOnlySelectedSetsAndIncludesEveryEnabledSet(int flags)
    {
        var options = new PasswordGeneratorOptions { Length = 32, Lowercase = (flags & 1) != 0,
            Uppercase = (flags & 2) != 0, Digits = (flags & 4) != 0, Symbols = (flags & 8) != 0 };
        var value = PasswordGenerator.Generate(options);
        Assert.Equal(32, value.Length);
        Assert.Equal(options.Lowercase, value.Any(char.IsLower));
        Assert.Equal(options.Uppercase, value.Any(char.IsUpper));
        Assert.Equal(options.Digits, value.Any(char.IsDigit));
        Assert.Equal(options.Symbols, value.Any(character => !char.IsLetterOrDigit(character)));
        Assert.DoesNotContain(value, character => "iloILO01".Contains(character));
    }

    [Fact]
    public void GeneratorRejectsEmptySelectionAndSupportsNumericPin()
    {
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordGeneratorOptions
        { Lowercase = false, Uppercase = false, Digits = false, Symbols = false }));
        var value = PasswordGenerator.Generate(new PasswordGeneratorOptions
        { Length = 4, Lowercase = false, Uppercase = false, Symbols = false, ExcludeSimilar = false });
        Assert.Equal(4, value.Length);
        Assert.All(value, character => Assert.True(char.IsDigit(character)));
    }

    [Fact]
    public void LegacyDatesMigrateToFieldsWithoutReplacingExplicitFieldDates()
    {
        var inherited = DateTimeOffset.UtcNow.AddDays(5);
        var own = inherited.AddDays(2);
        var topic = new SecretAssociation { Title = "Legacy", ExpiresAt = inherited,
            Fields = [new() { Description = "Inherited" }, new() { Description = "Own", ExpiresAt = own }] };
        var legacy = new VaultDocument { SchemaVersion = 3, Associations = [topic], Revisions = [new() { Association = topic }] };
        var repository = new VaultRepository(Path.Combine(folder, "vault.db"));
        var protectedValue = VaultCrypto.Protect(legacy, Password, VaultCrypto.LocalContext);
        try
        {
            repository.Write(protectedValue.Envelope);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(protectedValue.Key);
        }

        using var session = new VaultSession(repository);
        session.Unlock(Password);
        Assert.Equal(4, session.Document.SchemaVersion);
        var migrated = session.Document.Associations[0];
        Assert.Null(migrated.ExpiresAt);
        Assert.Equal(inherited, migrated.Fields[0].ExpiresAt);
        Assert.Equal(own, migrated.Fields[1].ExpiresAt);
        Assert.Equal(inherited, session.Document.Revisions[0].Association.Fields[0].ExpiresAt);
        session.Save(session.Document);
        session.Lock();
        session.Unlock(Password);
        Assert.Equal(own, session.Document.Associations[0].Fields[1].ExpiresAt);
        Assert.Equal(inherited, legacy.Associations[0].ExpiresAt);
    }

    [Fact]
    public void LegacyExportAndBackupDatesUpgradeBeforeImportOrRestore()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(3);
        var legacy = new VaultDocument { SchemaVersion = 3, Associations = [new() { Title = "Legacy transfer", ExpiresAt = expiry,
            Fields = [new() { Description = "Token", Kind = FieldKind.Secret, Value = "synthetic token" }] }] };
        var export = VaultCrypto.Protect(legacy, Password, VaultCrypto.ExportContext);
        var backup = VaultCrypto.Protect(legacy, Password, VaultCrypto.LocalContext);
        try
        {
            var imported = VaultSession.ReadExport(ExportPackage.Create(export.Envelope), Password);
            Assert.Equal(4, imported.SchemaVersion);
            Assert.Null(imported.Associations[0].ExpiresAt);
            Assert.Equal(expiry, imported.Associations[0].Fields[0].ExpiresAt);

            using var session = new VaultSession(new VaultRepository(Path.Combine(folder, "vault.db")));
            session.Create(Password);
            session.RestoreBackup(ExportPackage.Create(backup.Envelope), Password);
            session.Lock();
            session.Unlock(Password);
            Assert.Equal(4, session.Document.SchemaVersion);
            Assert.Null(session.Document.Associations[0].ExpiresAt);
            Assert.Equal(expiry, session.Document.Associations[0].Fields[0].ExpiresAt);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(export.Key);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(backup.Key);
        }
    }

    [Fact]
    public void GeneratorPreferencesPersistEncryptedAndInvalidPreferencesLeaveCurrentStateIntact()
    {
        using var session = new VaultSession(new VaultRepository(Path.Combine(folder, "vault.db")));
        session.Create(Password);
        var options = new PasswordGeneratorOptions { Length = 12, Symbols = false, Uppercase = false };
        session.Save(session.Document with { Settings = session.Document.Settings with { PasswordGenerator = options } });
        Assert.Throws<InvalidDataException>(() => session.Save(session.Document with { Settings = session.Document.Settings with
        { PasswordGenerator = options with { Lowercase = false, Digits = false } } }));
        session.Lock();
        session.Unlock(Password);
        Assert.Equal(options, session.Document.Settings.PasswordGenerator);
    }

    [Theory]
    [InlineData(0x53u, 3u, true)] [InlineData(0x87u, 6u, true)]
    [InlineData(0x53u, 0u, false)] [InlineData(0x53u, 4u, false)]
    [InlineData(0x53u, 16u, false)] [InlineData(0x3Au, 3u, false)]
    public void HotkeyGestureValidationPreventsBareTypingKeys(uint key, uint modifiers, bool valid)
    {
        Assert.Equal(valid, new HotkeyGesture(key, modifiers).IsValid);
    }

    [Fact]
    public void DevicePreferencesKeepShortcutWhenAutomaticUpdatesChange()
    {
        var value = new DevicePreferences { QuickAccess = new(0x87, 6) };
        value.Save(folder);
        (DevicePreferences.Load(folder) with { AutomaticUpdates = false }).Save(folder);
        var loaded = DevicePreferences.Load(folder);
        Assert.False(loaded.AutomaticUpdates);
        Assert.Equal(value.QuickAccess, loaded.QuickAccess);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }
}
