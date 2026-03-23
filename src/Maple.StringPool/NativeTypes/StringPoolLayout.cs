namespace Maple.StringPool.NativeTypes;

/// <summary>
/// Memory layout of the C++ <c>StringPool</c> object:
/// <code>
/// struct StringPool : ClassLevelLockable&lt;StringPool&gt;  // empty base
/// {
///     ZArray&lt;ZXString&lt;char&gt; *&gt;           m_apZMString;   // +0x00  (4 bytes — pointer)
///     ZArray&lt;ZXString&lt;unsigned short&gt; *&gt; m_apZWString;   // +0x04  (4 bytes — pointer)
///     ZFatalSection                        m_lock;         // +0x08  (8 bytes — TIB+ref)
/// };  // sizeof = 0x10
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <c>ClassLevelLockable&lt;StringPool&gt;</c> is an empty CRTP base with a static
/// <c>ms_nLocker</c> (volatile LONG) — contributes zero bytes to the instance layout.
/// </para>
/// <para>Static members reside at fixed addresses in .data — see <c>references.md</c>.</para>
/// </remarks>
public readonly ref struct StringPoolLayout
{
    /// <summary>Byte offset of <c>m_apZMString</c> (narrow string cache pointer).</summary>
    public const int NarrowCacheOffset = 0;

    /// <summary>Byte offset of <c>m_apZWString</c> (wide string cache pointer).</summary>
    public const int WideCacheOffset = TypeSizes.Pointer;

    /// <summary>Byte offset of <c>m_lock</c> (<see cref="ZFatalSectionLayout"/>).</summary>
    public const int LockOffset = TypeSizes.Pointer * 2;

    /// <summary>Total struct size: 0x10 (two pointers + <see cref="ZFatalSectionLayout.TotalBytes"/>).</summary>
    public const int TotalBytes = TypeSizes.Pointer * 2 + ZFatalSectionLayout.TotalBytes;
}

/// <summary>
/// Memory layout of <c>StringPool::Key</c>:
/// <code>
/// struct StringPool::Key {
///     ZArray&lt;unsigned char&gt; m_aKey;   // +0x00  (4 bytes — pointer to rotated key)
/// };
/// </code>
/// </summary>
public readonly ref struct StringPoolKeyLayout
{
    /// <summary>Byte offset of <c>m_aKey</c> (the <c>ZArray&lt;unsigned char&gt;</c> pointer).</summary>
    public const int KeyArrayOffset = 0;

    /// <summary>Total struct size (one pointer = 4 bytes).</summary>
    public const int TotalBytes = TypeSizes.Pointer;
}

/// <summary>
/// Layout of each encoded entry pointed to by <c>ms_aString[idx]</c>:
/// <code>
/// byte[0]     = nKeySeed  (signed — rotation amount for rotatel)
/// byte[1..]   = XOR-encrypted body, null-terminated
/// </code>
/// </summary>
public readonly ref struct EncodedEntryLayout
{
    /// <summary>Byte offset of the <c>nKeySeed</c> rotation byte.</summary>
    public const int SeedOffset = 0;

    /// <summary>Size of the seed field (1 byte).</summary>
    public const int SeedBytes = 1;

    /// <summary>Byte offset where the XOR-encrypted body begins.</summary>
    public const int BodyOffset = SeedBytes;
}
