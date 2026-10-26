using System.Security.Cryptography;
using SecretManager.Core.Models;
using SecretManager.Core.Security;
using SecretManager.Core.Services;
using SecretManager.Core.Storage;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class PartialTransferTests
{
    private static VaultDocument Sample() => new()
    {
        Associations = [new() { Title = "Example", Fields = [new() { Description = "Login", Value = "sample" },
            new() { Description = "Unselected secret", Kind = FieldKind.Secret, Value = "synthetic omitted value" }] },
            new() { Title = "Omitted association" }]
    };
    private static Dictionary<Guid, IReadOnlySet<Guid>> FirstField(VaultDocument doc) =>
        new() { [doc.Associations[0].Id] = new HashSet<Guid> { doc.Associations[0].Fields[0].Id } };

    [Fact]
    public void SelectionOmitsOtherFieldsAndTopicsWithoutMutatingSource()
    {
        var source = Sample(); var selected = VaultSelection.Select(source, FirstField(source));
        Assert.True(selected.IsPartial);
        Assert.Equal(2, selected.SchemaVersion);
        Assert.Single(selected.Associations); Assert.Single(selected.Associations[0].Fields);
        Assert.Equal(2, source.Associations.Count); Assert.Equal(2, source.Associations[0].Fields.Count);
        selected.Associations[0].Fields.Clear(); Assert.Equal(2, source.Associations[0].Fields.Count);
    }

    [Fact]
    public void FullSelectionPreservesFullTransferAndPartialMarkerCannotBeCleared()
    {
        var source = Sample();
        var all = source.Associations.ToDictionary(a => a.Id, a => (IReadOnlySet<Guid>)a.Fields.Select(f => f.Id).ToHashSet());
        Assert.False(VaultSelection.Select(source, all).IsPartial);
        Assert.True(VaultSelection.Select(source with { IsPartial = true }, all).IsPartial);
        Assert.Empty(VaultSelection.Select(new(), new Dictionary<Guid, IReadOnlySet<Guid>>()).Associations);
    }

    [Fact]
    public void PartialSchemaRequiresItsMarkerAndMergesIntoTheLocalFullSchema()
    {
        Assert.Throws<InvalidDataException>(() => VaultValidation.Validate(new() { SchemaVersion = 2 }));
        var local = Sample(); var partial = VaultSelection.Select(local, FirstField(local));
        var result = VaultMerge.Merge(local, partial, new Dictionary<Guid, ConflictResolution>());
        Assert.Equal(1, result.SchemaVersion); Assert.False(result.IsPartial);
        Assert.Equal(2, result.Associations[0].Fields.Count);
    }

    [Fact]
    public void InvalidOrEmptySelectionIsRejected()
    {
        var source = Sample(); var selection = FirstField(source);
        Assert.Throws<ArgumentException>(() => VaultSelection.Select(source, new Dictionary<Guid, IReadOnlySet<Guid>>()));
        selection[source.Associations[0].Id] = new HashSet<Guid> { Guid.NewGuid() };
        Assert.Throws<ArgumentException>(() => VaultSelection.Select(source, selection));
        Assert.Throws<ArgumentException>(() => VaultSelection.Select(source,
            new Dictionary<Guid, IReadOnlySet<Guid>> { [Guid.NewGuid()] = new HashSet<Guid>() }));
    }

    [Theory]
    [InlineData(ConflictResolution.KeepLocal)]
    [InlineData(ConflictResolution.UseImported)]
    [InlineData(ConflictResolution.KeepBoth)]
    public void PartialConflictChoicesPreserveOmittedLocalFields(ConflictResolution choice)
    {
        var source = Sample(); var selected = VaultSelection.Select(source, FirstField(source));
        selected.Associations[0].Fields[0] = selected.Associations[0].Fields[0] with { Value = "updated" };
        Assert.Single(VaultMerge.FindConflicts(source, selected));
        var merged = VaultMerge.Merge(source, selected, new Dictionary<Guid, ConflictResolution> { [selected.Associations[0].Id] = choice });
        Assert.Equal("synthetic omitted value", merged.Associations.Single(a => a.Id == source.Associations[0].Id).Fields[1].Value);
        Assert.Equal(choice == ConflictResolution.KeepBoth ? 3 : 2, merged.Associations.Count);
        var original = merged.Associations.Single(a => a.Id == source.Associations[0].Id);
        Assert.Equal(choice == ConflictResolution.UseImported ? "updated" : "sample", original.Fields[0].Value);
        Assert.False(merged.IsPartial); Assert.Equal(source.Settings, merged.Settings);
        Assert.Equal("sample", source.Associations[0].Fields[0].Value);
        if (choice == ConflictResolution.UseImported) Assert.Empty(VaultMerge.FindConflicts(merged, selected));
    }

    [Fact]
    public void PartialImportAddsFieldsByIdAndUnresolvedConflictsAreAtomic()
    {
        var source = Sample(); var selected = VaultSelection.Select(source, FirstField(source));
        selected.Associations[0].Fields.Add(new() { Description = "New field", Value = "new" });
        Assert.Throws<InvalidOperationException>(() => VaultMerge.Merge(source, selected, new Dictionary<Guid, ConflictResolution>()));
        Assert.Equal(2, source.Associations[0].Fields.Count);
        var result = VaultMerge.Merge(source, selected, new Dictionary<Guid, ConflictResolution> { [selected.Associations[0].Id] = ConflictResolution.UseImported });
        Assert.Equal(3, result.Associations.Single(a => a.Id == source.Associations[0].Id).Fields.Count);
    }

    [Fact]
    public void EightCharacterExportRoundTripsSelectionAndCannotUseLocalContext()
    {
        var path = Path.Combine(Path.GetTempPath(), "SecretManager.Tests", Guid.NewGuid().ToString(), "vault.db");
        try
        {
            using var session = new VaultSession(new VaultRepository(path)); session.Create("master password 123");
            var source = Sample(); session.Save(source);
            var json = session.Export("12345678", FirstField(source));
            Assert.DoesNotContain("synthetic omitted value", json);
            var imported = VaultSession.ReadExport(json, "12345678");
            Assert.True(imported.IsPartial); Assert.Single(imported.Associations); Assert.Single(imported.Associations[0].Fields);
            Assert.Equal(2, imported.SchemaVersion);
            Assert.ThrowsAny<CryptographicException>(() => VaultSession.ReadExport(json, "87654321"));
            Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Unprotect(ExportPackage.Read(json), "12345678", VaultCrypto.LocalContext));
            Assert.Throws<ArgumentException>(() => session.Export("1234567"));
            Assert.Throws<ArgumentException>(() => session.ChangePassword("master password 123", "12345678"));
            session.Lock(); session.Unlock("master password 123"); Assert.Equal(2, session.Document.Associations.Count);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(26, true)]
    [InlineData(9.9, false)]
    [InlineData(26.1, false)]
    [InlineData(double.NaN, false)]
    public void FontSizeBoundsAndComicSansAreValidated(double size, bool valid)
    {
        var doc = new VaultDocument { Settings = new() { FontSize = size, FontFamily = "Comic Sans MS" } };
        if (valid) VaultValidation.Validate(doc);
        else Assert.Throws<InvalidDataException>(() => VaultValidation.Validate(doc));
    }
}
