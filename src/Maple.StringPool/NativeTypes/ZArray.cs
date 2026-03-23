using System.Buffers.Binary;

namespace Maple.StringPool.NativeTypes;

/// <summary>
/// Mirrors <c>ZArray&lt;T&gt;</c>:
/// <code>
/// struct ZArray&lt;T&gt; {
///     T *a;   // +0x00  → points past count header
/// };
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The allocation is: <c>[int32 count][T[0], T[1], … T[count-1]]</c>.
/// The <c>a</c> pointer points at <c>T[0]</c>, not at the count.
/// The count lives at <c>a - sizeof(int)</c>.
/// </para>
/// <para>
/// In the PDB this is instantiated as:
/// <list type="bullet">
///   <item><c>ZArray&lt;ZXString&lt;char&gt; *&gt;</c> — <c>a</c> is <c>ZXString&lt;char&gt;**</c></item>
///   <item><c>ZArray&lt;ZXString&lt;unsigned short&gt; *&gt;</c></item>
///   <item><c>ZArray&lt;unsigned char&gt;</c> — <c>a</c> is <c>unsigned char*</c></item>
///   <item><c>ZArray&lt;long&gt;</c> — <c>a</c> is <c>int*</c></item>
/// </list>
/// </para>
/// </remarks>
/// <param name="elementCount">Number of elements in this array instance.</param>
public readonly ref struct ZArrayLayout(int elementCount)
{
    /// <summary>Offset of the element count field relative to the allocation base.</summary>
    public const int CountOffset = 0;

    /// <summary>Header size in bytes (one int32 count).</summary>
    public const int HeaderBytes = TypeSizes.Int32;

    /// <summary>Byte offset where array elements begin.</summary>
    public const int PayloadOffset = HeaderBytes;

    /// <summary>Number of elements in the array.</summary>
    public int ElementCount { get; } = elementCount;

    /// <summary>Total allocation size: header plus all elements of the given size.</summary>
    public int TotalBytes(int elementSize) => HeaderBytes + (ElementCount * elementSize);
}

/// <summary>
/// Typed reader for <c>ZArray&lt;T&gt;</c> elements from a binary image.
/// </summary>
public static class ZArray
{
    /// <summary>
    /// Reads the element count from the allocation header.
    /// <c>a</c> points at the payload; count is at <c>a - 4</c>.
    /// </summary>
    public static int ReadCount(ReadOnlySpan<byte> image, int payloadFileOffset) =>
        BinaryPrimitives.ReadInt32LittleEndian(image[(payloadFileOffset - ZArrayLayout.HeaderBytes)..]);

    /// <summary>
    /// Reads all <c>uint32</c> pointer elements from a <c>ZArray&lt;T*&gt;</c>.
    /// </summary>
    public static uint[] ReadPointerElements(ReadOnlySpan<byte> image, int payloadFileOffset, int count)
    {
        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            int elementOffset = payloadFileOffset + (i * TypeSizes.Pointer);
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(image[elementOffset..]);
        }
        return result;
    }

    /// <summary>
    /// Reads all byte elements from a <c>ZArray&lt;unsigned char&gt;</c>.
    /// </summary>
    public static byte[] ReadByteElements(ReadOnlySpan<byte> image, int payloadFileOffset, int count) =>
        image.Slice(payloadFileOffset, count).ToArray();
}
