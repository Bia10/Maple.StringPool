namespace Maple.StringPool.Source;

/// <summary>
/// <see cref="IPeImageReader"/> backed by the entire PE image loaded into
/// <see cref="ReadOnlyMemory{T}"/>.
/// </summary>
/// <remarks>
/// Suitable for offline analysis where loading the full binary into managed memory
/// is acceptable. GMS v95 MapleStory.exe is approximately 17 MB.
/// The decoder derives all <see cref="ReadOnlySpan{T}"/> views directly from
/// <see cref="Image"/> — no per-read dispatch overhead.
/// </remarks>
public sealed class MemoryPeImageReader : IPeImageReader
{
    private readonly ReadOnlyMemory<byte> _image;

    private MemoryPeImageReader(ReadOnlyMemory<byte> image) => _image = image;

    /// <summary>
    /// Opens <paramref name="path"/> and reads the entire PE image into managed memory.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="IOException">An I/O error occurred while reading the file.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have the required permission.</exception>
    public static MemoryPeImageReader FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new MemoryPeImageReader(File.ReadAllBytes(path));
    }

    /// <summary>Wraps an already-loaded PE image byte array without copying.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    public static MemoryPeImageReader FromBytes(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new(new ReadOnlyMemory<byte>(image));
    }

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> Image => _image;

    /// <inheritdoc/>
    public void Dispose() { /* no-op: backing array is managed memory; no unmanaged resources to release */
    }
}
