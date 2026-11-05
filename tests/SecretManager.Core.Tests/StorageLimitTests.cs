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

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }
}
