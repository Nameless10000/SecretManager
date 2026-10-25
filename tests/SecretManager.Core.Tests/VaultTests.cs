using System.Security.Cryptography;
using SecretManager.Core.Models;
using SecretManager.Core.Security;
using SecretManager.Core.Services;
using SecretManager.Core.Storage;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class VaultTests : IDisposable
{
    private const string Password = "long test password 123";
    private readonly string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SecretManager.Tests", Guid.NewGuid().ToString());
    private VaultRepository Repository => new(System.IO.Path.Combine(folder, "vault.db"));
    private static VaultDocument Sample() => new()
    {
        Associations = [new SecretAssociation
        {
            Title = "Почта Example",
            Fields = [new SecretField { Description = "Пароль", Value = "unique-secret-123", Kind = FieldKind.Secret },
                new SecretField { Description = "Ключ", Value = Convert.ToBase64String(new byte[] { 0, 1, 2, 255 }),
                    Kind = FieldKind.File, FileName = "key.bin" }]
        }]
    };

    [Fact]
    public void ReopeningPreservesAssociationsAndBase64Files()
    {
        using (var session = new VaultSession(Repository)) { session.Create(Password); session.Save(Sample()); }
        using var reopened = new VaultSession(Repository);
        Assert.False(reopened.IsUnlocked);
        reopened.Unlock(Password);
        Assert.Equal("unique-secret-123", reopened.Document.Associations[0].Fields[0].Value);
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, Convert.FromBase64String(reopened.Document.Associations[0].Fields[1].Value));
    }

    [Fact]
    public void SqliteContainsNeitherTitlesNorValues()
    {
        using var session = new VaultSession(Repository);
        session.Create(Password); session.Save(Sample()); session.Lock();
        var raw = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Repository.Path));
        Assert.DoesNotContain("unique-secret-123", raw);
        Assert.DoesNotContain("Почта Example", raw);
        Assert.DoesNotContain("key.bin", raw);
    }

    [Fact]
    public void WrongPasswordDoesNotUnlock()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Lock();
        Assert.ThrowsAny<CryptographicException>(() => session.Unlock("wrong password"));
        Assert.False(session.IsUnlocked);
        Assert.Throws<InvalidOperationException>(() => session.Search(""));
    }

    [Fact]
    public void SearchMatchesCyrillicCaseAndPrefixesWithoutSearchingSecrets()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample());
        Assert.Single(session.Search("ПОЧТ exa"));
        Assert.Empty(session.Search("unique-secret"));
        Assert.Empty(session.Search("\" OR *")); // OR is a literal token, never an operator.
    }

    [Fact]
    public void PunctuationCannotInjectFtsQuery()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample());
        Assert.Empty(session.Search("x OR y"));
        Assert.Single(session.Search("***\""));
    }

    [Fact]
    public void ExportIsPortableAndUsesAnIndependentPasswordAndContext()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample());
        var exported = session.Export("independent export password");
        Assert.DoesNotContain("unique-secret-123", exported);
        var document = VaultSession.ReadExport(exported, "independent export password");
        Assert.Equal("Почта Example", document.Associations[0].Title);
        Assert.ThrowsAny<CryptographicException>(() => VaultSession.ReadExport(exported, Password));
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Unprotect(ExportPackage.Read(exported),
            "independent export password", VaultCrypto.LocalContext));
    }

    [Fact]
    public void TamperingWithCiphertextIsRejected()
    {
        var value = VaultCrypto.Protect(Sample(), Password, VaultCrypto.ExportContext);
        try
        {
            var cipher = Convert.FromBase64String(value.Envelope.Ciphertext); cipher[0] ^= 1;
            Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Unprotect(value.Envelope with
            { Ciphertext = Convert.ToBase64String(cipher) }, Password, VaultCrypto.ExportContext));
        }
        finally { CryptographicOperations.ZeroMemory(value.Key); }
    }

    [Fact]
    public void ExportChecksumDetectsChangesAndRecomputedChecksumCannotBypassAuthentication()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample());
        var exported = session.Export(Password);
        var package = System.Text.Json.JsonSerializer.Deserialize<ExportPackage>(exported)!;
        var envelope = VaultCrypto.Parse(package.Payload);
        var bytes = Convert.FromBase64String(envelope.Ciphertext); bytes[0] ^= 1;
        var modified = envelope with { Ciphertext = Convert.ToBase64String(bytes) };
        var wrongChecksum = System.Text.Json.JsonSerializer.Serialize(package with { Payload = System.Text.Json.JsonSerializer.Serialize(modified) });
        Assert.Throws<InvalidDataException>(() => VaultSession.ReadExport(wrongChecksum, Password));
        var recomputedChecksum = ExportPackage.Create(modified);
        Assert.ThrowsAny<CryptographicException>(() => VaultSession.ReadExport(recomputedChecksum, Password));
    }

    [Fact]
    public void EveryEncryptionUsesADifferentNonce()
    {
        var first = VaultCrypto.Protect(Sample(), Password, VaultCrypto.LocalContext);
        try
        {
            var second = VaultCrypto.Encrypt(Sample(), first.Key, Convert.FromBase64String(first.Envelope.Salt), VaultCrypto.LocalContext);
            Assert.NotEqual(first.Envelope.Nonce, second.Nonce);
        }
        finally { CryptographicOperations.ZeroMemory(first.Key); }
    }

    [Fact]
    public void InvalidSavePreservesDiskAndCurrentSession()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample());
        var invalid = new VaultDocument { Associations = [new SecretAssociation { Title = "" }] };
        Assert.Throws<InvalidDataException>(() => session.Save(invalid));
        Assert.Equal("Почта Example", session.Document.Associations[0].Title);
        session.Lock(); session.Unlock(Password);
        Assert.Equal("Почта Example", session.Document.Associations[0].Title);
    }

    [Fact]
    public void PasswordChangeVerifiesOldPasswordAndPersistsAtomically()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample());
        Assert.ThrowsAny<CryptographicException>(() => session.ChangePassword("bad", "new password 12345"));
        session.ChangePassword(Password, "new password 12345"); session.Lock();
        Assert.ThrowsAny<CryptographicException>(() => session.Unlock(Password));
        session.Unlock("new password 12345"); Assert.Single(session.Document.Associations);
    }

    [Fact]
    public void LockRemovesSearchAndBlocksExportAndSave()
    {
        using var session = new VaultSession(Repository); session.Create(Password); session.Save(Sample()); session.Lock();
        Assert.Throws<InvalidOperationException>(() => session.Document);
        Assert.Throws<InvalidOperationException>(() => session.Export(Password));
        Assert.Throws<InvalidOperationException>(() => session.Save(Sample()));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("            ")]
    public void WeakPasswordsAreRejected(string password) => Assert.Throws<ArgumentException>(() => VaultCrypto.ValidateNewPassword(password));

    [Fact]
    public void MalformedEnvelopeAndExcessiveKdfWorkAreRejectedBeforeDerivation()
    {
        Assert.Throws<InvalidDataException>(() => VaultCrypto.Parse("{}"));
        Assert.Throws<InvalidDataException>(() => VaultCrypto.Parse("not json"));
        var envelope = new EncryptedEnvelope(1, int.MaxValue, "", "", "", "");
        Assert.Throws<InvalidDataException>(() => VaultCrypto.Unprotect(envelope, Password, VaultCrypto.ExportContext));
    }

    [Fact]
    public void BrokenBase64AndTraversalFileNamesAreRejected()
    {
        var field = Sample().Associations[0].Fields[1];
        var topic = Sample().Associations[0] with { Fields = [field with { Value = "not base64" }] };
        Assert.Throws<InvalidDataException>(() => VaultValidation.Validate(new() { Associations = [topic] }));
        topic = topic with { Fields = [field with { FileName = "../key.bin" }] };
        Assert.Throws<InvalidDataException>(() => VaultValidation.Validate(new() { Associations = [topic] }));
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
