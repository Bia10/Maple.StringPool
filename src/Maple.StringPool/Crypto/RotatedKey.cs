using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Maple.StringPool.Crypto;

/// <summary>
/// Per-entry cipher key: a rotated copy of <c>StringPool::ms_aKey</c> held
/// entirely on the execution stack via a <c>[InlineArray]</c> backing buffer.
/// Zero heap allocations — lifetime is bounded to the enclosing decode call.
/// </summary>
internal ref struct RotatedKey
{
    private KeyBuffer _buffer;
    private readonly int _length;

    /// <summary>
    /// Copies <paramref name="masterKey"/> into the inline buffer and applies
    /// <see cref="StringPoolCrypto.RotateLeftInPlace"/> with the given <paramref name="seed"/>.
    /// </summary>
    /// <param name="masterKey">The static <c>StringPool::ms_aKey</c> bytes.</param>
    /// <param name="seed">
    ///   Signed rotation amount — the byte at index 0 of the encoded entry,
    ///   forwarded directly to <see cref="StringPoolCrypto.RotateLeftInPlace"/>.
    /// </param>
    internal RotatedKey(ReadOnlySpan<byte> masterKey, int seed)
    {
        if (masterKey.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(masterKey), "Master key length must be greater than zero.");

        // Lower bound (> 0) is guaranteed by StringPoolDecoder's _keySize > 0 validation.
        // Upper bound enforced here because KeyBuffer is fixed at 256 bytes.
        if (masterKey.Length > 256)
            throw new ArgumentOutOfRangeException(
                nameof(masterKey),
                masterKey.Length,
                "Master key length must not exceed 256 bytes (KeyBuffer capacity)."
            );

        _length = masterKey.Length;
        Span<byte> active = MemoryMarshal.CreateSpan(ref _buffer[0], _length);
        masterKey.CopyTo(active);
        StringPoolCrypto.RotateLeftInPlace(active, seed);
    }

    /// <summary>
    /// Returns the key byte at cipher position <paramref name="position"/>,
    /// wrapping modulo the key length.
    /// </summary>
    internal readonly byte this[int position]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer[position % _length];
    }
}

/// <summary>
/// Fixed-size 256-byte inline buffer backing <see cref="RotatedKey"/>.
/// Kept <see langword="internal"/> rather than <c>file</c>-scoped: C# <c>file</c>-scoped
/// types may not appear in field declarations of types with assembly-wide visibility.
/// </summary>
[InlineArray(256)]
internal struct KeyBuffer
{
    private byte _element;
}
