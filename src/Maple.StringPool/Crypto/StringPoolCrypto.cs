using System.Runtime.CompilerServices;

namespace Maple.StringPool.Crypto;

/// <summary>
/// Pure-static implementation of the <c>StringPool</c> cipher.
/// All methods operate exclusively on <see cref="Span{T}"/> — no heap allocation.
/// </summary>
internal static class StringPoolCrypto
{
    /// <summary>
    /// Circular left-rotation of <paramref name="key"/> by <paramref name="shift"/>
    /// bits, performed in-place.
    /// </summary>
    /// <remarks>
    /// Two-pass algorithm:
    /// <list type="number">
    ///   <item>
    ///     <term>Pass 1 — element rotation</term>
    ///     <description>
    ///       Rotates array elements left by <c>shift &gt;&gt; 3</c> positions using a
    ///       <c>stackalloc</c> scratch buffer. Skipped when <c>shift &lt; 8</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Pass 2 — bit rotation</term>
    ///     <description>
    ///       Left-shifts each byte by <c>shift &amp; 7</c> bits. The carry spilled from
    ///       <c>key[0]</c> wraps into <c>key[^1]</c>; skipped for single-byte keys.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    internal static void RotateLeftInPlace(Span<byte> key, int shift)
    {
        if (key.IsEmpty || shift == 0)
            return;

        int length = key.Length;

        // Pass 1 — element-level rotation: left by (shift >> 3) positions.
        if ((uint)shift >= 8)
        {
            // Cast to uint before the modulo so that negative seeds (e.g. seed = -1 → shift = -1,
            // shift >> 3 = -1) wrap correctly: (uint)(-1) = 0xFFFFFFFF, and 0xFFFFFFFF % 16 = 15,
            // giving a left-rotation of 15 positions — matching the original runtime behaviour.
            int elementPositions = (int)((uint)(shift >> 3) % (uint)length);
            if (elementPositions != 0)
            {
                // Scratch fits on the stack; key size is validated to ≤ 256 bytes.
                Span<byte> scratch = stackalloc byte[length];
                key.CopyTo(scratch);
                for (int i = 0; i < length; i++)
                    key[i] = scratch[(i + elementPositions) % length];
            }
        }

        // Pass 2 — bit-level rotation: left by (shift & 7) bits.
        int bitCount = shift & 7;
        if (bitCount == 0)
            return;

        // Carry: high bits of key[0] that wrap into key[^1].
        // Skipped for single-element keys (matches original runtime behavior).
        byte wrappedHighBits = length > 1 ? (byte)(key[0] >> (8 - bitCount)) : (byte)0;

        for (int i = 0; i < length; i++)
        {
            byte highBitsFromNext = i < length - 1 ? (byte)(key[i + 1] >> (8 - bitCount)) : (byte)0;

            key[i] = (byte)((key[i] << bitCount) | highBitsFromNext);
        }

        key[^1] |= wrappedHighBits;
    }

    /// <summary>
    /// Decrypts <paramref name="encryptedBody"/> into <paramref name="plainBuffer"/>
    /// using the zero-collision XOR rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="encryptedBody"/> and <paramref name="plainBuffer"/> must be the
    /// same length; the caller is responsible for pre-sizing the output buffer.
    /// </para>
    /// <para>
    /// <b>Zero-collision rule:</b> when <c>encrypted[i] == keyByte</c>, the output is
    /// <c>keyByte</c> rather than <c>0</c>. This avoids embedding false null terminators
    /// in the encrypted stream.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Decode(
        scoped ReadOnlySpan<byte> encryptedBody,
        scoped ref RotatedKey key,
        scoped Span<byte> plainBuffer
    )
    {
        int count = encryptedBody.Length;
        for (int i = 0; i < count; i++)
        {
            byte keyByte = key[i];
            byte encrypted = encryptedBody[i];
            // Zero-collision: enc == key → preserve key byte (avoids false nulls); otherwise XOR.
            plainBuffer[i] = encrypted == keyByte ? keyByte : (byte)(encrypted ^ keyByte);
        }
    }
}
