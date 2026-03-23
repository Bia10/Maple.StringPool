namespace Maple.StringPool;

/// <summary>
/// Pre-built <see cref="StringPoolAddresses"/> for known MapleStory client versions.
/// See <c>references.md</c> for the full PDB-verified address table.
/// </summary>
public static class KnownLayouts
{
    /// <summary>
    /// <see cref="StringPoolAddresses"/> for GMS v95 (<c>MapleStory.exe</c>, image base <c>0x400000</c>).
    /// <list type="bullet">
    ///   <item><c>StringPool::ms_aString</c> — <c>const char*[6883]</c> pointer table in .data</item>
    ///   <item><c>StringPool::ms_aKey</c> — <c>const unsigned char[16]</c> static XOR master key</item>
    ///   <item><c>StringPool::ms_nKeySize</c> — always 16 for v95</item>
    ///   <item><c>StringPool::ms_nSize</c> — 6883 (0x1AE3) string slots</item>
    /// </list>
    /// </summary>
    public static readonly StringPoolAddresses GmsV95 = new()
    {
        ImageBase = 0x400000u,
        MsAString = 0xC5A878u,
        MsAKey = 0xB98830u,
        MsNKeySize = 0xB98840u,
        MsNSize = 0xB98844u,
    };
}
