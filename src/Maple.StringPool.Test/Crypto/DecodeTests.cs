using System.Text;
using Maple.StringPool.Crypto;

namespace Maple.StringPool.Test.Crypto;

/// <summary>
/// Unit tests for the XOR decode step (<c>anonymous::Decode&lt;char&gt;</c> at 0x746520)
/// exercised through <see cref="StringPoolCrypto.Decode"/> and the full
/// <see cref="RotatedKey"/> pipeline.
/// </summary>
/// <remarks>
/// The zero-collision rule: if <c>encrypted[i] == keyByte</c> the decoder
/// outputs <c>keyByte</c> (identity), avoiding false null-terminator collisions.
/// All other bytes are decrypted via <c>encrypted ^ keyByte</c>.
/// </remarks>
public sealed class DecodeTests
{
    // ── Helpers — isolate ref struct usage from async state machine ───────────

    private static string DecodeWithKey(byte[] encBody, byte[] masterKey, int seed = 0)
    {
        RotatedKey key = new(masterKey, seed);
        Span<byte> plain = stackalloc byte[encBody.Length];
        StringPoolCrypto.Decode(encBody, ref key, plain);
        return Encoding.Latin1.GetString(plain);
    }

    private static byte[] KeyBytes(params byte[] bytes) => bytes;

    // ── Zero-collision (identity) path ────────────────────────────────────────

    [Test]
    public async Task Decode_WhenEncEqualsKey_OutputsKeyByte()
    {
        // plain='T' (0x54), key=0x54 → encode: p==k → enc=0x54
        // decode: enc==key → output=key=0x54='T'
        string result = DecodeWithKey([0x54], KeyBytes(0x54));

        await Assert.That(result).IsEqualTo("T");
    }

    // ── XOR path ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Decode_XorPath_SingleByte()
    {
        // plain='T' (0x54), key=0x4A → enc=0x54^0x4A=0x1E
        // decode: 0x1E ^ 0x4A = 0x54 = 'T'
        string result = DecodeWithKey([0x1E], KeyBytes(0x4A));

        await Assert.That(result).IsEqualTo("T");
    }

    [Test]
    public async Task Decode_MultiByteWord_WithKeyWrapping()
    {
        // plain="HERO" = [0x48, 0x45, 0x52, 0x4F]
        // key=[0x01, 0x02, 0x03, 0x04], seed=0
        // enc=[0x49, 0x47, 0x51, 0x4B]  (pre-computed)
        string result = DecodeWithKey([0x49, 0x47, 0x51, 0x4B], KeyBytes(0x01, 0x02, 0x03, 0x04));

        await Assert.That(result).IsEqualTo("HERO");
    }

    [Test]
    public async Task Decode_AllZeroKey_PassthroughForNonZeroBytes()
    {
        // key=0x00: enc[i]=plain[i]^0=plain[i] (since plain != 0 for non-null chars)
        // 'A'=0x41, 'B'=0x42
        string result = DecodeWithKey([0x41, 0x42], KeyBytes(0x00, 0x00));

        await Assert.That(result).IsEqualTo("AB");
    }

    // ── Seed / rotation integration ───────────────────────────────────────────

    [Test]
    public async Task Decode_WithSeed8_UsesRotatedKey()
    {
        // master=[0x41, 0x42], seed=8 → rotate 1 element → rotated=[0x42, 0x41]
        // plain="AB" = [0x41, 0x42]
        // enc: 0x41==0x42? no → 0x41^0x42=0x03; 0x42==0x41? no → 0x42^0x41=0x03
        // enc = [0x03, 0x03]
        // decode with rotated key: 0x03^0x42=0x41='A', 0x03^0x41=0x42='B'
        string result = DecodeWithKey([0x03, 0x03], KeyBytes(0x41, 0x42), seed: 8);

        await Assert.That(result).IsEqualTo("AB");
    }

    // ── Empty body ────────────────────────────────────────────────────────────

    [Test]
    public async Task Decode_EmptyBody_ReturnsEmptyString()
    {
        string result = DecodeWithKey([], KeyBytes(0x4A));

        await Assert.That(result).IsEqualTo(string.Empty);
    }
}
