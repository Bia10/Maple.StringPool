namespace Maple.StringPool.Source;

/// <summary>
/// Read-only view over a flat PE image binary, used by <see cref="StringPoolDecoder"/>
/// to fetch raw bytes at computed file offsets.
/// </summary>
/// <remarks>
/// Decouples the decoder from any specific I/O strategy (file, memory, memory-mapped,
/// test double). The decoder obtains <see cref="Image"/> once at construction and
/// derives all <see cref="ReadOnlySpan{T}"/> views from it — no per-field dispatch.
/// </remarks>
public interface IPeImageReader : IDisposable
{
    /// <summary>
    /// The full PE image as a contiguous read-only memory region.
    /// <para>
    /// The decoder works entirely via <see cref="ReadOnlySpan{T}"/> derived from
    /// this property: <see cref="System.Buffers.Binary.BinaryPrimitives"/> for
    /// struct reads, <c>MemoryExtensions.IndexOf</c> for null-terminator
    /// search, and range-based slicing in place of per-read method calls.
    /// </para>
    /// </summary>
    ReadOnlyMemory<byte> Image { get; }
}
