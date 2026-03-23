using System.Buffers.Binary;
using Maple.StringPool.Source;

namespace Maple.StringPool.Test.Helpers;

/// <summary>
/// Flat-buffer <see cref="IPeImageReader"/> for unit testing.
/// Blobs are written directly into an underlying <c>byte[]</c> via
/// <see cref="WriteAt"/> / <see cref="WriteUInt32At"/>; unwritten bytes are zero.
/// </summary>
/// <remarks>
/// Buffer size (0x900000 = 9 MB) comfortably covers all test addresses:
/// the highest address used by <c>StringPoolDecoderTests</c> is the
/// <c>ms_aString</c> pointer table entry for slot 25 at ~0x85A8D8.
/// </remarks>
internal sealed class StubPeImageReader : IPeImageReader
{
    // 9 MB flat buffer — zeroed by default, spans derived on demand.
    private readonly byte[] _buffer = new byte[0x900000];

    // ── Write helpers ─────────────────────────────────────────────────────────

    /// <summary>Copies <paramref name="data"/> into the buffer at <paramref name="offset"/>.</summary>
    public void WriteAt(long offset, ReadOnlySpan<byte> data) => data.CopyTo(_buffer.AsSpan()[(int)offset..]);

    /// <summary>Writes a little-endian <see cref="uint"/> at <paramref name="offset"/>.</summary>
    public void WriteUInt32At(long offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan()[(int)offset..], value);

    // ── IPeImageReader ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Image => new(_buffer);

    /// <inheritdoc />
    public void Dispose() { }
}
