using System.Security.Cryptography;
using System.Text;
using SecretManager.Core.Models;
using SecretManager.Core.Security;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class ExportReadingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonSeekableExportWithBomPreservesChecksumAndEncoding(bool unicode)
    {
        var protectedValue = VaultCrypto.Protect(new VaultDocument(), "synthetic export password 123", VaultCrypto.ExportContext);
        try
        {
            var json = ExportPackage.Create(protectedValue.Envelope);
            var encoding = unicode ? Encoding.Unicode : new UTF8Encoding(true);
            using var stream = new CountingStream(encoding.GetPreamble().Concat(encoding.GetBytes(json)).ToArray());
            var actual = await ExportPackage.ReadTextAsync(stream);
            Assert.Equal(json, actual); Assert.Equal(protectedValue.Envelope, ExportPackage.Read(actual));
        }
        finally { CryptographicOperations.ZeroMemory(protectedValue.Key); }
    }
    [Fact]
    public async Task GrowingInputStopsAtLimitPlusOneWithoutTrustingFileMetadata()
    {
        using var stream = new CountingStream(new byte[1024]);
        await Assert.ThrowsAsync<InvalidDataException>(() => ExportPackage.ReadTextAsync(stream, 32));
        Assert.Equal(33, stream.BytesRead);
    }
    [Fact]
    public async Task CancelledExportReadStopsBeforeAnyDataIsRead()
    {
        using var stream = new CountingStream(new byte[1024]);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExportPackage.ReadTextAsync(stream, cancellationToken: cancelled.Token));
        Assert.Equal(0, stream.BytesRead);
    }
    private sealed class CountingStream(byte[] data) : MemoryStream(data)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException("No trustworthy metadata.");
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken); BytesRead += read; return read;
        }
    }
}
