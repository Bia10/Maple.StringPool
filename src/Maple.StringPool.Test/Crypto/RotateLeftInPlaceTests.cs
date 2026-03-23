using Maple.StringPool.Crypto;

namespace Maple.StringPool.Test.Crypto;

/// <summary>
/// Unit tests for <c>StringPoolCrypto.RotateLeftInPlace</c> (0x746270),
/// the circular left-rotation applied to the master key during per-entry
/// key derivation.
/// </summary>
/// <remarks>
/// Test vectors are computed analytically from the two-pass algorithm:
/// <list type="number">
///   <item><b>Pass 1</b> — element rotation: left-shift array elements by
///     <c>shift &gt;&gt; 3</c> positions.</item>
///   <item><b>Pass 2</b> — bit rotation: left-shift each byte by
///     <c>shift &amp; 7</c>, with carry wrapping <c>[0]</c>'s high bits
///     into <c>[^1]</c>.</item>
/// </list>
/// </remarks>
public sealed class RotateLeftInPlaceTests
{
    // ── Identity ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Shift0_LeavesKeyUnchanged()
    {
        byte[] key = [0xAA, 0xBB, 0xCC, 0xDD];
        StringPoolCrypto.RotateLeftInPlace(key, 0);

        await Assert.That(Convert.ToHexString(key)).IsEqualTo("AABBCCDD");
    }

    [Test]
    public async Task EmptySpan_ShiftAny_NoException()
    {
        byte[] key = [];
        StringPoolCrypto.RotateLeftInPlace(key, 42);

        await Assert.That(key.Length).IsEqualTo(0);
    }

    // ── Element rotation (Pass 1) ─────────────────────────────────────────────

    [Test]
    public async Task Shift8_RotatesOneElementLeft()
    {
        // [AA BB CC DD] → [BB CC DD AA]
        byte[] key = [0xAA, 0xBB, 0xCC, 0xDD];
        StringPoolCrypto.RotateLeftInPlace(key, 8);

        await Assert.That(Convert.ToHexString(key)).IsEqualTo("BBCCDDAA");
    }

    [Test]
    public async Task Shift16_RotatesTwoElementsLeft()
    {
        // [AA BB CC DD] → [CC DD AA BB]
        byte[] key = [0xAA, 0xBB, 0xCC, 0xDD];
        StringPoolCrypto.RotateLeftInPlace(key, 16);

        await Assert.That(Convert.ToHexString(key)).IsEqualTo("CCDDAABB");
    }

    [Test]
    public async Task Shift8_OnV95MasterKey_RotatesFirstByteToEnd()
    {
        // v95 master key (synthetic stand-in with real structure)
        //   [6C, EB, BC, 5E, 4B, CC, 47, 5D, 3E, 3B, C2, 90, 82, DA, 69, 83]
        // After rotate-1: first byte (6C) moves to position [15]
        byte[] key = [0x6C, 0xEB, 0xBC, 0x5E, 0x4B, 0xCC, 0x47, 0x5D, 0x3E, 0x3B, 0xC2, 0x90, 0x82, 0xDA, 0x69, 0x83];
        StringPoolCrypto.RotateLeftInPlace(key, 8);

        // First four bytes of result should be EB BC 5E 4B
        await Assert.That(Convert.ToHexString(key[..4])).IsEqualTo("EBBC5E4B");
        // Original [0] (6C) should now be at [15]
        await Assert.That(key[15]).IsEqualTo((byte)0x6C);
    }

    // ── Bit rotation (Pass 2) ─────────────────────────────────────────────────

    [Test]
    public async Task Shift1_RotatesBitsLeft()
    {
        // [0x82, 0x01]
        //   wrap = 0x82 >> 7 = 1
        //   key[0] = (0x82 << 1) | (0x01 >> 7) = 0x04 | 0 = 0x04
        //   key[1] = (0x01 << 1) | 0 = 0x02 ; then |= wrap → 0x03
        byte[] key = [0x82, 0x01];
        StringPoolCrypto.RotateLeftInPlace(key, 1);

        await Assert.That(Convert.ToHexString(key)).IsEqualTo("0403");
    }

    // ── Combined element + bit rotation ───────────────────────────────────────

    [Test]
    public async Task Shift9_CombinesElementAndBitRotation()
    {
        // [AA, BB]:  element rotate 1 → [BB, AA]
        //   wrap = 0xBB >> 7 = 1
        //   key[0] = (0xBB << 1) | (0xAA >> 7) = 0x76 | 0x01 = 0x77
        //   key[1] = (0xAA << 1) = 0x54 ; then |= 1 → 0x55
        byte[] key = [0xAA, 0xBB];
        StringPoolCrypto.RotateLeftInPlace(key, 9);

        await Assert.That(Convert.ToHexString(key)).IsEqualTo("7755");
    }
}
