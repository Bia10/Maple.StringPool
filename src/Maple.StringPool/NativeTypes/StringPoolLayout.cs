namespace Maple.StringPool.NativeTypes;

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
