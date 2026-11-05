using System.Security.Cryptography;
using System.Text;
using SecretManager.App.ViewModels;
using SecretManager.Core.Models;
using SecretManager.Core.Security;
using SecretManager.Core.Services;
using SecretManager.Core.Storage;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class ProductivityTests : IDisposable
{
    private const string Password = "synthetic master password 123";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SecretManager.Productivity", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(folder, "vault.db");

    [Theory]
    [InlineData(12, false, false)]
    [InlineData(24, true, true)]
    [InlineData(128, true, false)]
    public void GeneratedPasswordsRespectConstraints(int length, bool symbols, bool similar)
    {
        var values = Enumerable.Range(0, 100).Select(_ => PasswordGenerator.Generate(length, symbols, similar)).ToArray();
        Assert.Equal(100, values.Distinct().Count());
        foreach (var value in values)
        {
            Assert.Equal(length, value.Length);
            Assert.Contains(value, char.IsUpper);
            Assert.Contains(value, char.IsLower);
            Assert.Contains(value, char.IsDigit);
            Assert.Equal(symbols, value.Any(character => !char.IsLetterOrDigit(character)));
            if (similar)
            {
                Assert.DoesNotContain(value, character => "iloILO01".Contains(character));
            }
        }

        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(3));
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(129));
    }

    [Fact]
    public void TemplatesAreIndependentAndAuditNeverContainsValues()
    {
        var first = AssociationTemplates.Create("First", 1);
        var second = AssociationTemplates.Create("Second", 1);
        Assert.NotEqual(first.Fields[0].Id, second.Fields[0].Id);
        first = first with { Fields = [new() { Description = "Secret", Kind = FieldKind.Secret, Value = "fake123" }] };
        second = second with { Fields = [new() { Description = "Secret", Kind = FieldKind.Secret, Value = "fake123" }] };
        var document = new VaultDocument { Associations = [first, second] };
        var findings = VaultInsights.Audit(document);
        Assert.Equal(4, findings.Count);
        Assert.DoesNotContain("fake123", string.Join("\n", findings));
        Assert.Empty(VaultInsights.Audit(new()));
        foreach (var index in Enumerable.Range(0, AssociationTemplates.Names.Count))
        {
            VaultValidation.Validate(new() { Associations = [AssociationTemplates.Create("Template", index)] });
        }
    }

    [Fact]
    public void ReminderWindowIncludesExpiredAndSevenDaysButNotUndated()
    {
        var now = DateTimeOffset.UtcNow;
        var document = new VaultDocument { Associations = [new() { Title = "Topic", Fields = [
            new() { Description = "Expired", ExpiresAt = now.AddDays(-1) },
            new() { Description = "Soon", ExpiresAt = now.AddDays(7) },
            new() { Description = "Later", ExpiresAt = now.AddDays(8) },
            new() { Description = "None" }] }] };
        Assert.Equal(new[] { "Expired", "Soon" }, VaultInsights.Due(document, now).Select(item => item.Description));
    }

    [Fact]
    public async Task FavoritesTagsHistoryAndTrashSurviveReopenAndLock()
    {
        using var model = new MainViewModel(Database);
        await model.UnlockAsync(Password, Password);
        var topic = AssociationTemplates.Create("Server", 2) with { IsFavorite = true, Tags = ["work"], ExpiresAt = DateTimeOffset.UtcNow };
        model.SaveAssociation(topic);
        model.SaveAssociation(new() { Title = "Other" });
        model.Query = "wor";
        Assert.Equal(topic.Id, Assert.Single(model.Associations).Model.Id);
        model.Query = "";
        model.FavoritesOnly = true;
        model.Refresh();
        Assert.Single(model.Associations);
        model.FavoritesOnly = false;
        model.SaveField(topic.Id, topic.Fields[0] with { Value = "original synthetic value" });
        model.SaveField(topic.Id, topic.Fields[0] with { Value = "new synthetic value" });
        var revision = model.TransferDocument.Revisions.First(item => item.Association.Fields.Any(field => field.Value == "original synthetic value"));
        model.RestoreRevision(revision.Id);
        Assert.Equal("original synthetic value", model.TransferDocument.Associations.Single(item => item.Id == topic.Id).Fields.Single(field => field.Id == topic.Fields[0].Id).Value);
        model.DeleteAssociation(topic.Id);
        var deleted = model.TransferDocument.Revisions.First(item => item.Deleted && item.Association.Id == topic.Id);
        model.Lock();
        Assert.Empty(model.Associations);
        Assert.Throws<InvalidOperationException>(() => model.TransferDocument);
        await model.UnlockAsync(Password, "");
        model.RestoreRevision(deleted.Id);
        Assert.True(model.TransferDocument.Associations.Single(item => item.Id == topic.Id).IsFavorite);
        Assert.Equal(4, model.TransferDocument.SchemaVersion);
    }

    [Fact]
    public void EncryptedBackupRestoresAndWrongPasswordLeavesDocumentUntouched()
    {
        using var session = new VaultSession(new VaultRepository(Database));
        session.Create(Password);
        var topic = new SecretAssociation { Title = "private-topic", Tags = ["private-tag"],
            Fields = [new() { Description = "Secret", Kind = FieldKind.Secret, Value = "private-synthetic-value" }] };
        session.Save(new() { Associations = [topic] });
        session.Backup(true);
        var backup = Directory.GetFiles(session.BackupDirectory).OrderByDescending(File.GetLastWriteTimeUtc).First();
        var json = File.ReadAllText(backup);
        Assert.DoesNotContain("private-topic", json);
        Assert.DoesNotContain("private-tag", json);
        Assert.DoesNotContain("private-synthetic-value", Encoding.UTF8.GetString(File.ReadAllBytes(Database)));
        session.Save(session.Document with { Associations = [] });
        Assert.ThrowsAny<CryptographicException>(() => session.RestoreBackup(json, "wrong"));
        Assert.Empty(session.Document.Associations);
        session.ChangePassword(Password, "new synthetic password 123");
        session.RestoreBackup(json, Password);
        Assert.Equal(topic.Title, Assert.Single(session.Document.Associations).Title);
        session.Lock();
        session.Unlock("new synthetic password 123");
        Assert.Equal(topic.Id, Assert.Single(session.Document.Associations).Id);
    }

    [Fact]
    public void ExportsNeverIncludeHistoryOrDeletedSecretsAndBackupRetentionIsBounded()
    {
        using var session = new VaultSession(new VaultRepository(Database));
        session.Create(Password);
        var topic = AssociationTemplates.Create("Deleted", 1);
        session.Save(new() { Associations = [topic] });
        session.Save(session.Document with { Associations = [] });
        Assert.NotEmpty(session.Document.Revisions);
        var exported = VaultSession.ReadExport(session.Export("export test password"), "export test password");
        Assert.Empty(exported.Revisions);
        Assert.Empty(exported.Associations);
        for (var index = 0; index < 15; index++)
        {
            session.Backup(true);
        }

        Assert.Equal(10, Directory.GetFiles(session.BackupDirectory, "*.smbackup").Length);
    }

    [Fact]
    public void InvalidMetadataAndRestoreConflictDoNotLoseCurrentData()
    {
        using var session = new VaultSession(new VaultRepository(Database));
        session.Create(Password);
        var topic = new SecretAssociation { Title = "First" };
        session.Save(new() { Associations = [topic] });
        Assert.Throws<InvalidDataException>(() => session.Save(session.Document with { Associations = [topic with { Tags = [new string('x', 51)] }] }));
        session.Save(session.Document with { Associations = [topic with { Title = "Renamed" }, new() { Title = "First" }] });
        var revision = session.Document.Revisions.First(item => item.Association.Title == "First");
        Assert.Throws<ArgumentException>(() => session.RestoreRevision(revision.Id));
        Assert.Equal(2, session.Document.Associations.Count);
        var imported = session.Document with { Associations = [topic with { Title = "Renamed", IsFavorite = true }] };
        Assert.Single(VaultMerge.FindConflicts(session.Document, imported));
    }

    [Fact]
    public void OversizedHistoryForcesEncryptedBackupEvenWhenPeriodicBackupsAreDisabled()
    {
        using var session = new VaultSession(new VaultRepository(Database));
        session.Create(Password);
        var content = new string('x', 1_000_000);
        var topic = new SecretAssociation { Title = "Large", Fields = Enumerable.Range(0, 18)
            .Select(index => new SecretField { Description = "Field " + index, Value = content }).ToList() };
        session.Save(new() { Associations = [topic], Settings = new() { AutomaticBackups = false } });
        session.Save(session.Document with { Associations = [] });
        var file = Directory.GetFiles(session.BackupDirectory, "*.smbackup").OrderByDescending(File.GetLastWriteTimeUtc).First();
        session.RestoreBackup(File.ReadAllText(file), Password);
        Assert.Equal(18, Assert.Single(session.Document.Associations).Fields.Count);
    }

    [Fact]
    public void CorruptBackupAndFailedBackupWriteLeaveActiveAndPersistedDocumentUnchanged()
    {
        using var session = new VaultSession(new VaultRepository(Database));
        session.Create(Password);
        var topic = new SecretAssociation { Title = "Keep me" };
        session.Save(new() { Associations = [topic], Settings = new() { AutomaticBackups = false } });
        Assert.Throws<InvalidDataException>(() => session.RestoreBackup("broken", Password));
        if (Directory.Exists(session.BackupDirectory))
        {
            Directory.Delete(session.BackupDirectory, true);
        }

        File.WriteAllText(session.BackupDirectory, "synthetic directory obstruction");
        session.Save(session.Document with { Settings = session.Document.Settings with { AutomaticBackups = true } });
        Assert.ThrowsAny<IOException>(() => session.Save(session.Document with { Associations = [] }));
        Assert.Equal(topic.Id, Assert.Single(session.Document.Associations).Id);
        session.Lock();
        session.Unlock(Password);
        Assert.Equal(topic.Id, Assert.Single(session.Document.Associations).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }
}
