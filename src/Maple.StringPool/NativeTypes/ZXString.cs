using System.Buffers.Binary;

namespace Maple.StringPool.NativeTypes;

/// <summary>
/// Mirrors <c>ZXString&lt;char&gt;::_ZXStringData</c>:
/// <code>
/// struct _ZXStringData {
///     int nRef;       // +0x00  reference count
///     int nCap;       // +0x04  allocated capacity
///     int nByteLen;   // +0x08  payload byte length (excluding null terminator)
/// };
/// // immediately followed by char[] payload + null terminator
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <c>ZXString&lt;T&gt;._m_pStr</c> points past the header, directly at the payload.
/// The header lives at <c>_m_pStr - sizeof(_ZXStringData)</c>.
/// </para>
/// <para>
/// Wide variant <c>ZXString&lt;unsigned short&gt;::_ZXStringData</c> (line 56548)
/// has identical layout; <c>nByteLen</c> stores byte length regardless of character width.
/// </para>
/// </remarks>
/// <param name="payloadBytes">Payload byte length (excluding the null terminator); used to compute <see cref="TotalBytes"/>.</param>
public readonly ref struct ZXStringDataLayout(int payloadBytes)
{
    /// <summary>Byte offset of <c>nRef</c> within the header.</summary>
    public const int RefCountOffset = 0;

    /// <summary>Byte offset of <c>nCap</c> within the header.</summary>
    public const int CapacityOffset = TypeSizes.Int32;

    /// <summary>Byte offset of <c>nByteLen</c> within the header.</summary>
    public const int ByteLengthOffset = TypeSizes.Int32 * 2;

    /// <summary>Total header size in bytes (3 × int32 = 12).</summary>
    public const int HeaderBytes = TypeSizes.Int32 * 3;

    /// <summary>Byte offset where the character payload begins (same as <see cref="HeaderBytes"/>).</summary>
    public const int PayloadOffset = HeaderBytes;

    /// <summary>Size of the null terminator following the payload.</summary>
    public const int NullTerminatorBytes = 1;

    private readonly int _payloadBytes = payloadBytes;

    /// <summary>Total allocation size: header + payload + null terminator.</summary>
    public int TotalBytes => HeaderBytes + _payloadBytes + NullTerminatorBytes;
}

/// <summary>
/// Mirrors <c>ZXString&lt;char&gt;</c>:
/// <code>
/// struct ZXString&lt;char&gt; {
///     char *_m_pStr;   // +0x00  → points at payload (past _ZXStringData header)
/// };
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The underlying memory starts with a <see cref="ZXStringDataLayout"/> header
/// (nRef, nCap, nByteLen), immediately followed by the character payload and a
/// null terminator. <c>_m_pStr</c> points at the payload, not the allocation base.
/// </para>
/// <para>
/// This C# representation stores the decoded <see cref="string"/> value directly —
/// the header fields are available for debugging via separate read methods.
/// </para>
/// </remarks>
public readonly struct ZXString
{
    /// <summary>Decoded string payload.</summary>
    public string Value { get; }

    /// <summary>Reference count from the <c>_ZXStringData</c> header.</summary>
    public int RefCount { get; }

    /// <summary>Allocated capacity from the <c>_ZXStringData</c> header.</summary>
    public int Capacity { get; }

    /// <summary>Byte length from the <c>_ZXStringData</c> header.</summary>
    public int ByteLength { get; }

    /// <summary>Creates a <see cref="ZXString"/> with the specified value and optional header metadata.</summary>
    /// <param name="value">Decoded string payload; must not be <see langword="null"/>.</param>
    /// <param name="refCount">Reference count from the <c>_ZXStringData</c> header; defaults to 1.</param>
    /// <param name="capacity">Allocated capacity; defaults to <c>value.Length</c> when zero.</param>
    /// <param name="byteLength">Byte length from <c>nByteLen</c>; defaults to <c>value.Length</c> when zero.</param>
    public ZXString(string value, int refCount = 1, int capacity = 0, int byteLength = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
        RefCount = refCount;
        Capacity = capacity > 0 ? capacity : value.Length;
        ByteLength = byteLength > 0 ? byteLength : value.Length;
    }

    /// <summary>
    /// Reads a <c>ZXString&lt;char&gt;</c> from binary data at the given
    /// pointer (<c>_m_pStr</c>) file offset.
    /// </summary>
    /// <param name="image">Raw PE image bytes.</param>
    /// <param name="payloadFileOffset">
    ///   File offset of <c>_m_pStr</c> (the payload, not the allocation base).
    /// </param>
    public static ZXString ReadFrom(ReadOnlySpan<byte> image, int payloadFileOffset)
    {
        int headerBase = payloadFileOffset - ZXStringDataLayout.HeaderBytes;

        int refCount = BinaryPrimitives.ReadInt32LittleEndian(
            image[(headerBase + ZXStringDataLayout.RefCountOffset)..]
        );
        int capacity = BinaryPrimitives.ReadInt32LittleEndian(
            image[(headerBase + ZXStringDataLayout.CapacityOffset)..]
        );
        int byteLen = BinaryPrimitives.ReadInt32LittleEndian(
            image[(headerBase + ZXStringDataLayout.ByteLengthOffset)..]
        );

        string payload = System.Text.Encoding.Latin1.GetString(image.Slice(payloadFileOffset, byteLen));

        return new ZXString(payload, refCount, capacity, byteLen);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Implicit conversion to <see cref="string"/> returning the decoded payload.</summary>
    public static implicit operator string(ZXString s) => s.Value;
}
