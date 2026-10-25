using SecretManager.Core.Models;
using Xunit;

namespace SecretManager.Core.Tests;

public sealed class AttachmentTests
{
    [Fact]
    public void BinaryFilesAreStoredAsBase64WithoutChangingBytes()
    {
        byte[] bytes = [0, 1, 255, 128, 0];
        var result = AttachmentContent.FromBytes("key.bin", bytes, false);
        Assert.Equal(bytes, Convert.FromBase64String(result.Base64));
        Assert.Equal(bytes.Length, result.Length); Assert.Equal("application/octet-stream", result.ContentType);
    }
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder\\file.txt")]
    [InlineData("")]
    public void InvalidNamesAreRejected(string name) => Assert.Throws<InvalidDataException>(() => AttachmentContent.FromBytes(name, [1], false));
    [Fact]
    public void ImageModeRejectsOtherFilesAndAttachmentSizeIsBounded()
    {
        Assert.Throws<InvalidDataException>(() => AttachmentContent.FromBytes("fake.png", [1, 2, 3], true));
        Assert.Throws<InvalidDataException>(() => AttachmentContent.FromBytes("huge.bin", new byte[VaultValidation.MaxAttachmentBytes + 1], false));
        Assert.Empty(AttachmentContent.FromBytes("empty.txt", [], false).Base64);
    }
    [Fact]
    public async Task StreamReadAcceptsExactLimitAndRejectsGrowthWithoutReadingTheWholeFile()
    {
        using var exact = new MemoryStream(new byte[8]);
        Assert.Equal(8, (await AttachmentContent.ReadBoundedAsync(exact, 8)).Length);
        using var longer = new MemoryStream(new byte[1000]);
        await Assert.ThrowsAsync<InvalidDataException>(() => AttachmentContent.ReadBoundedAsync(longer, 8));
        Assert.Equal(9, longer.Position);
    }
    [Fact]
    public async Task StreamReadHonorsCancellation()
    {
        using var source = new MemoryStream([1, 2, 3]); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentContent.ReadBoundedAsync(source, 8, cancellation.Token));
    }
}
