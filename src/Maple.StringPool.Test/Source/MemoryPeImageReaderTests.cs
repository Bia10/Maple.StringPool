using Maple.StringPool.Source;

namespace Maple.StringPool.Test.Source;

/// <summary>
/// Unit tests for <see cref="MemoryPeImageReader"/>: factory methods,
/// <see cref="MemoryPeImageReader.Image"/> property, and <see cref="IDisposable.Dispose"/>.
/// </summary>
public sealed class MemoryPeImageReaderTests
{
    // ── FromFile ──────────────────────────────────────────────────────────────

    [Test]
    public async Task FromFile_NullPath_ThrowsArgumentNullException()
    {
        await Assert.That(() => MemoryPeImageReader.FromFile(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task FromFile_NonExistentFile_ThrowsFileNotFoundException()
    {
        string path = Path.Combine(Path.GetTempPath(), "maple_test_no_such_file_1a2b3c.exe");

        await Assert.That(() => MemoryPeImageReader.FromFile(path)).Throws<FileNotFoundException>();
    }

    [Test]
    public async Task FromFile_ValidFile_ImageContentsMatchFile()
    {
        byte[] expected = [0xDE, 0xAD, 0xBE, 0xEF];
        string tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpFile, expected);

            using MemoryPeImageReader reader = MemoryPeImageReader.FromFile(tmpFile);

            await Assert.That(reader.Image.Span.SequenceEqual(expected)).IsTrue();
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    // ── FromBytes ─────────────────────────────────────────────────────────────

    [Test]
    public async Task FromBytes_NullImage_ThrowsArgumentNullException()
    {
        await Assert.That(() => MemoryPeImageReader.FromBytes(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task FromBytes_ValidBytes_ImageMatchesInput()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];

        using MemoryPeImageReader reader = MemoryPeImageReader.FromBytes(data);

        await Assert.That(reader.Image.Span.SequenceEqual(data)).IsTrue();
    }

    // ── Image property ────────────────────────────────────────────────────────

    [Test]
    public async Task Image_ReturnsCorrectLengthAndContents()
    {
        byte[] data = [0xAA, 0xBB, 0xCC];

        using MemoryPeImageReader reader = MemoryPeImageReader.FromBytes(data);

        ReadOnlyMemory<byte> image = reader.Image;

        await Assert.That(image.Length).IsEqualTo(3);
        await Assert.That(image.Span[0]).IsEqualTo((byte)0xAA);
        await Assert.That(image.Span[1]).IsEqualTo((byte)0xBB);
        await Assert.That(image.Span[2]).IsEqualTo((byte)0xCC);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Dispose_IsNoOp_DoesNotThrow()
    {
        var reader = MemoryPeImageReader.FromBytes([0x00]);
        reader.Dispose(); // backing is managed memory — no-op

        await Assert.That(reader.GetType()).IsNotNull();
    }

    [Test]
    public async Task Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var reader = MemoryPeImageReader.FromBytes([0x00]);
        reader.Dispose();
        reader.Dispose(); // second call must also be safe

        await Assert.That(reader.GetType()).IsNotNull();
    }
}
