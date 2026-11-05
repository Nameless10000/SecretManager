using SecretManager.Core.Models;
using SecretManager.Core.Services;
using SecretManager.Core.Storage;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class StorageLimitTests : IDisposable
{
    private const string Password = "synthetic storage limit password";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "SecretManager.LimitTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(64, true)]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public void StorageSettingHasBoundedRange(int megabytes, bool valid)
    {
        var document = new VaultDocument { Settings = new() { MaxStorageMegabytes = megabytes } };
        if (valid)
        {
            VaultValidation.Validate(document);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => VaultValidation.Validate(document));
        }
    }

    [Fact]
    public void ShrinkingBelowCurrentSizeIsAtomicAndLargerLimitSurvivesReopenAndExport()
    {
        var repository = new VaultRepository(Path.Combine(folder, "vault.db"));
        using var session = new VaultSession(repository);
        session.Create(Password);
        var content = new string('a', 1_000_000);
        var fields = Enumerable.Range(0, 18).Select(index => new SecretField { Description = "Field " + index, Value = content }).ToList();
        session.Save(new() { Associations = [new() { Title = "Large", Fields = fields }] });
        Assert.Throws<InvalidDataException>(() => session.Save(session.Document with
        { Settings = session.Document.Settings with { MaxStorageMegabytes = 16 } }));
        Assert.Equal(64, session.Document.Settings.MaxStorageMegabytes);
        session.Lock();
        session.Unlock(Password);
        Assert.Equal(64, session.Document.Settings.MaxStorageMegabytes);
        Assert.Equal(18, session.Document.Associations[0].Fields.Count);
        session.Save(session.Document with { Settings = session.Document.Settings with { MaxStorageMegabytes = 512 } });
        var exported = VaultSession.ReadExport(session.Export("synthetic export password"), "synthetic export password");
        Assert.Equal(512, exported.Settings.MaxStorageMegabytes);
        session.Lock();
        session.Unlock(Password);
        Assert.Equal(512, session.Document.Settings.MaxStorageMegabytes);
    }

    [Theory]
    [InlineData(16, 16777216, true)]
    [InlineData(16, 16777217, false)]
    [InlineData(64, 67108864, true)]
    [InlineData(64, 67108865, false)]
    [InlineData(512, 536870912, true)]
    [InlineData(512, 536870913, false)]
    public void SerializedByteBoundaryIsExactWithoutAllocatingTheWholeQuota(int megabytes, int bytes, bool valid)
    {
        if (valid)
        {
            VaultValidation.ValidateDocumentSize(bytes, megabytes);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => VaultValidation.ValidateDocumentSize(bytes, megabytes));
        }
    }

    [Fact]
    public void EncryptedEnvelopeLargerThanLegacy64MegabytesRoundTrips()
    {
        var value = new string('s', 1_000_000);
        var document = new VaultDocument
        {
            Settings = new() { MaxStorageMegabytes = 128 },
            Associations = [new() { Title = "Large synthetic vault", Fields = Enumerable.Range(0, 70)
                .Select(index => new SecretField { Description = "Field " + index, Value = value }).ToList() }]
        };
        var encrypted = SecretManager.Core.Security.VaultCrypto.Protect(document, Password, SecretManager.Core.Security.VaultCrypto.LocalContext);
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(encrypted.Envelope);
            var parsed = SecretManager.Core.Security.VaultCrypto.Parse(json);
            var opened = SecretManager.Core.Security.VaultCrypto.Unprotect(parsed, Password, SecretManager.Core.Security.VaultCrypto.LocalContext);
            try
            {
                Assert.Equal(70, opened.Document.Associations[0].Fields.Count);
                Assert.Equal(value, opened.Document.Associations[0].Fields[69].Value);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(opened.Key);
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(encrypted.Key);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }
}
