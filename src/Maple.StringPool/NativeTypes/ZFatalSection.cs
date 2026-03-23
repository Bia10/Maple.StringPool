namespace Maple.StringPool.NativeTypes;

/// <summary>
/// Mirrors <c>ZFatalSectionData</c>:
/// <code>
/// struct ZFatalSectionData {
///     void *_m_pTIB;   // +0x00  thread information block pointer
///     int   _m_nRef;   // +0x04  reentrance count
/// };
/// </code>
/// And <c>ZFatalSection : ZFatalSectionData</c> (empty derived struct).
/// </summary>
/// <remarks>
/// <para>
/// <c>ZFatalSection</c> is a reentrant critical section used within <c>StringPool</c>
/// to guard access to the lazy caches. It is <b>not</b> a Win32 CRITICAL_SECTION;
/// it stores only the owning thread's TIB and a reference count.
/// </para>
/// <para>
/// Lock protocol (from <c>ZFatalSection::Lock</c> / unlock inlined in GetString):
/// <list type="bullet">
///   <item><c>Lock</c>: checks if <c>_m_pTIB</c> matches <c>NtCurrentTeb()</c>;
///     if so increments <c>_m_nRef</c> (reentrant). Otherwise spins until released.</item>
///   <item><c>Unlock</c>: decrements <c>_m_nRef</c>; if it reaches 0 sets
///     <c>_m_pTIB = NULL</c>.</item>
/// </list>
/// </para>
/// </remarks>
public readonly ref struct ZFatalSectionLayout
{
    /// <summary>Byte offset of <c>_m_pTIB</c> (thread information block pointer).</summary>
    public const int TibPointerOffset = 0;

    /// <summary>Byte offset of <c>_m_nRef</c> (reentrance count).</summary>
    public const int RefCountOffset = TypeSizes.Pointer;

    /// <summary>Total struct size in bytes (pointer + int32 = 8).</summary>
    public const int TotalBytes = TypeSizes.Pointer + TypeSizes.Int32;
}

/// <summary>
/// C# representation of <c>ZFatalSection</c>.
/// For offline analysis contexts the lock state is meaningless;
/// this type exists for structural completeness.
/// </summary>
/// <param name="tibPointer">Runtime TIB pointer; zero when the lock is unlocked.</param>
/// <param name="refCount">Reentrance count; zero when fully unlocked.</param>
public readonly struct ZFatalSection(uint tibPointer, int refCount)
{
    /// <summary>Runtime TIB pointer — null when unlocked.</summary>
    public uint TibPointer { get; } = tibPointer;

    /// <summary>Reentrance count — 0 when fully unlocked.</summary>
    public int RefCount { get; } = refCount;

    /// <summary>Creates an unlocked section matching the constructor default.</summary>
    public static ZFatalSection Unlocked => new(0, 0);
}
