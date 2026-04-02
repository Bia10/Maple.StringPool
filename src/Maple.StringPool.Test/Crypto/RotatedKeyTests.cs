using System.Text;
using Maple.StringPool.Crypto;

namespace Maple.StringPool.Test.Crypto;

/// <summary>
/// Unit tests for <see cref="RotatedKey"/>: constructor validation and indexer wrapping.
/// </summary>
/// <remarks>
/// Because <see cref="RotatedKey"/> is a <c>ref struct</c>, its usage is wrapped
/// in private synchronous helper methods following the same pattern used in
/// <see cref="DecodeTests"/>.
/// </remarks>
public sealed class RotatedKeyTests
{
    // ── Constructor validation ────────────────────────────────────────────────

    private static void CreateKey(byte[] masterKey, int seed)
    {
        RotatedKey _ = new(masterKey, seed);
    }

    [Test]
    public async Task Constructor_OversizedMasterKey_ThrowsArgumentOutOfRangeException()
    {
        byte[] bigKey = new byte[257]; // > 256 (KeyBuffer capacity)

        await Assert.That(() => CreateKey(bigKey, 0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_ExactlyMaximumKeySize_DoesNotThrow()
    {
        byte[] maxKey = new byte[256]; // exactly 256 — boundary must succeed

        await Assert.That(() => CreateKey(maxKey, 0)).ThrowsNothing();
    }

    [Test]
    public async Task Constructor_EmptyMasterKey_ThrowsArgumentOutOfRangeException()
    {
        await Assert.That(() => CreateKey([], 0)).Throws<ArgumentOutOfRangeException>();
    }

    // ── Indexer — key wrapping ——————————————————————————————————————————————

    // ReadOnlySpan<byte> params: call sites can pass compile-time span literals — zero heap.
    private static byte DecodeByteAt(
        ReadOnlySpan<byte> masterKey,
        ReadOnlySpan<byte> encBody,
        int outputIndex,
        int seed = 0
    )
    {
        RotatedKey key = new(masterKey, seed);
        Span<byte> plain = stackalloc byte[encBody.Length];
        StringPoolCrypto.Decode(encBody, in key, plain);
        return plain[outputIndex];
    }

    [Test]
    public async Task Indexer_PositionEqualToKeyLength_WrapsToStart()
    {
        // key = [0xAA, 0xBB], seed = 0 (no rotation).
        // index 2 → 2 % 2 = 0 → key[0] = 0xAA.
        // enc[2] = 0x00 (≠ 0xAA) → plain[2] = 0x00 ^ 0xAA = 0xAA.
        byte result = DecodeByteAt([0xAA, 0xBB], [0x00, 0x00, 0x00], outputIndex: 2);

        await Assert.That(result).IsEqualTo((byte)0xAA);
    }

    [Test]
    public async Task Indexer_LargePositionWrapsCorrectly()
    {
        // key = [0x01, 0x02, 0x03], seed = 0.
        // index 7 → 7 % 3 = 1 → key[1] = 0x02.
        // enc[7] = 0x00 → plain[7] = 0x00 ^ 0x02 = 0x02.
        byte result = DecodeByteAt(
            [0x01, 0x02, 0x03],
            [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
            outputIndex: 7
        );

        await Assert.That(result).IsEqualTo((byte)0x02);
    }

    // ── Integration: full decode with RotatedKey ──────────────────────────────

    // ReadOnlySpan<byte> params: call sites can pass compile-time span literals — zero heap.
    private static string DecodeString(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> encBody, int seed = 0)
    {
        RotatedKey key = new(masterKey, seed);
        Span<byte> plain = stackalloc byte[encBody.Length];
        StringPoolCrypto.Decode(encBody, in key, plain);
        return Encoding.Latin1.GetString(plain);
    }

    [Test]
    public async Task Decode_SingleByteKey_RepeatsForAllPositions()
    {
        // Single-byte key [0x41], no rotation.
        // enc=[0x00, 0x00, 0x00] → plain=[0x41, 0x41, 0x41] = "AAA"
        string result = DecodeString([0x41], [0x00, 0x00, 0x00]);

        await Assert.That(result).IsEqualTo("AAA");
    }
}
