using Maple.StringPool.Crypto;
using Maple.StringPool.NativeTypes;

namespace Maple.StringPool;

/// <summary>
/// Static <c>.data</c> section addresses for a StringPool binary.
/// Each member corresponds to a static field in the C++ <c>StringPool</c> class.
/// </summary>
/// <remarks>
/// <para>
/// These addresses point to plain C arrays and scalars embedded directly
/// in the PE <c>.data</c> segment — they are <b>not</b>
/// <see cref="ZArrayLayout"/> heap allocations. The <c>ZArray</c> type is used
/// only for the runtime caches (<c>m_apZMString</c>, <c>m_apZWString</c>),
/// not the static encoded data.
/// </para>
/// <para>
/// The decoder reads from these four addresses during bootstrap:
/// <list type="bullet">
///   <item><see cref="MsAString"/> — <c>const char*[N]</c> pointer table of
///         <see cref="EncodedEntryLayout"/> entries</item>
///   <item><see cref="MsAKey"/> — <c>const unsigned char[K]</c> master XOR key,
///         copied into per-entry <see cref="RotatedKey"/> instances</item>
///   <item><see cref="MsNKeySize"/> — <c>const unsigned int</c> key byte count</item>
///   <item><see cref="MsNSize"/> — <c>const unsigned int</c> total slot count</item>
/// </list>
/// </para>
/// </remarks>
public readonly record struct StringPoolAddresses
{
    /// <summary>PE image base address (typically 0x400000 for GMS x86 clients).</summary>
    public required uint ImageBase { get; init; }

    /// <summary>
    /// <c>StringPool::ms_aString</c> — <c>const char*[N]</c> pointer table.
    /// Each element is an x86 pointer (stride = <see cref="TypeSizes.Pointer"/>)
    /// targeting an <see cref="EncodedEntryLayout"/>: <c>[seed_byte][xor_body\0]</c>.
    /// </summary>
    public required uint MsAString { get; init; }

    /// <summary>
    /// <c>StringPool::ms_aKey</c> — <c>const unsigned char[K]</c> static XOR master key.
    /// Copied and left-rotated per entry by <see cref="RotatedKey"/>.
    /// </summary>
    public required uint MsAKey { get; init; }

    /// <summary>
    /// <c>StringPool::ms_nKeySize</c> — <c>const unsigned int</c> (key length in bytes).
    /// </summary>
    public required uint MsNKeySize { get; init; }

    /// <summary>
    /// <c>StringPool::ms_nSize</c> — <c>const unsigned int</c> (total string-slot count).
    /// </summary>
    public required uint MsNSize { get; init; }
}
