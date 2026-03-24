using System.Buffers.Binary;
using Maple.StringPool.NativeTypes;

namespace Maple.StringPool.Test.NativeTypes;

/// <summary>
/// Unit tests for <see cref="ZArrayLayout"/>, <see cref="ZArray"/>,
/// <see cref="ZFatalSection"/>, <see cref="ZXStringDataLayout"/>, and <see cref="ZXString"/>.
/// </summary>
public sealed class NativeTypesTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // ZArrayLayout
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ZArrayLayout_ElementCount_StoresConstructorValue()
    {
        var layout = new ZArrayLayout(10);

        await Assert.That(layout.ElementCount).IsEqualTo(10);
    }

    [Test]
    public async Task ZArrayLayout_TotalBytes_ReturnsHeaderPlusPayload()
    {
        // Header = 4 bytes, 5 pointer-sized elements = 5 × 4 = 20 bytes → total 24.
        var layout = new ZArrayLayout(5);

        await Assert.That(layout.TotalBytes(TypeSizes.Pointer)).IsEqualTo(24);
    }

    [Test]
    public async Task ZArrayLayout_ZeroElements_TotalBytesEqualsHeader()
    {
        var layout = new ZArrayLayout(0);

        await Assert.That(layout.TotalBytes(TypeSizes.Pointer)).IsEqualTo(ZArrayLayout.HeaderBytes);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ZArray
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ZArray_ReadCount_ReturnsInt32FromHeaderBeforePayload()
    {
        // Layout: [int32 count=42 at bytes 0-3][payload starts at byte 4]
        await Assert.That(ReadCountFromStub()).IsEqualTo(42);
    }

    // Sync helper — stackalloc avoids a heap array for the 8-byte image.
    private static int ReadCountFromStub()
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 42);
        return ZArray.ReadCount(buf, payloadFileOffset: 4);
    }

    [Test]
    public async Task ZArray_ReadPointerElements_ReturnsAllUInt32Values()
    {
        // 3 pointer-sized elements at offset 0.
        (uint a, uint b, uint c) = ReadThreePointerElementsFromStub();

        await Assert.That(a).IsEqualTo(0xDEAD_BEEFu);
        await Assert.That(b).IsEqualTo(0xCAFE_BABEu);
        await Assert.That(c).IsEqualTo(0x1234_5678u);
    }

    // Sync helper — stackalloc avoids a heap array for the 12-byte image.
    private static (uint A, uint B, uint C) ReadThreePointerElementsFromStub()
    {
        Span<byte> buf = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, 0xDEAD_BEEFu);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[4..], 0xCAFE_BABEu);
        BinaryPrimitives.WriteUInt32LittleEndian(buf[8..], 0x1234_5678u);
        uint[] elements = ZArray.ReadPointerElements(buf, payloadFileOffset: 0, count: 3);
        return (elements[0], elements[1], elements[2]);
    }

    [Test]
    public async Task ZArray_ReadByteElements_ReturnsCopy()
    {
        // ReadOnlySpan<byte> literal — stored in the PE's read-only data section, zero heap.
        byte[] result = ZArray.ReadByteElements([0xAA, 0xBB, 0xCC, 0xDD], payloadFileOffset: 1, count: 2);

        await Assert.That(result[0]).IsEqualTo((byte)0xBB);
        await Assert.That(result[1]).IsEqualTo((byte)0xCC);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ZFatalSection
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ZFatalSection_Constructor_StoresProperties()
    {
        var section = new ZFatalSection(tibPointer: 0x12345678u, refCount: 3);

        await Assert.That(section.TibPointer).IsEqualTo(0x12345678u);
        await Assert.That(section.RefCount).IsEqualTo(3);
    }

    [Test]
    public async Task ZFatalSection_Unlocked_HasZeroValues()
    {
        ZFatalSection section = ZFatalSection.Unlocked;

        await Assert.That(section.TibPointer).IsEqualTo(0u);
        await Assert.That(section.RefCount).IsEqualTo(0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ZXStringDataLayout
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ZXStringDataLayout_TotalBytes_HeaderPlusPayloadPlusNull()
    {
        // Header = 12 bytes, payload = 5 bytes, null terminator = 1 byte → 18 total.
        var layout = new ZXStringDataLayout(5);

        await Assert.That(layout.TotalBytes).IsEqualTo(18);
    }

    [Test]
    public async Task ZXStringDataLayout_EmptyPayload_TotalBytesIsThirteen()
    {
        // Header = 12, payload = 0, null = 1 → 13.
        var layout = new ZXStringDataLayout(0);

        await Assert.That(layout.TotalBytes).IsEqualTo(13);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ZXString
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ZXString_Constructor_SetsAllProperties()
    {
        var s = new ZXString("Hello", refCount: 2, capacity: 8, byteLength: 5);

        await Assert.That(s.Value).IsEqualTo("Hello");
        await Assert.That(s.RefCount).IsEqualTo(2);
        await Assert.That(s.Capacity).IsEqualTo(8);
        await Assert.That(s.ByteLength).IsEqualTo(5);
    }

    [Test]
    public async Task ZXString_Constructor_NullValue_ThrowsArgumentNullException()
    {
        await Assert.That(() => new ZXString(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ZXString_Constructor_ZeroCapacity_DefaultsToStringLength()
    {
        var s = new ZXString("Hello", capacity: 0);

        await Assert.That(s.Capacity).IsEqualTo(5);
    }

    [Test]
    public async Task ZXString_Constructor_ZeroByteLength_DefaultsToStringLength()
    {
        var s = new ZXString("Hello", byteLength: 0);

        await Assert.That(s.ByteLength).IsEqualTo(5);
    }

    [Test]
    public async Task ZXString_ToString_ReturnsPayloadValue()
    {
        var s = new ZXString("World");

        await Assert.That(s.ToString()).IsEqualTo("World");
    }

    [Test]
    public async Task ZXString_ImplicitStringConversion_ReturnsValue()
    {
        var s = new ZXString("Hero");
        string result = s; // Uses the implicit operator string(ZXString)

        await Assert.That(result).IsEqualTo("Hero");
    }

    [Test]
    public async Task ZXString_ReadFrom_ParsesHeaderAndPayloadCorrectly()
    {
        ZXString result = ParseHelloFromStub();

        await Assert.That(result.Value).IsEqualTo("Hello");
        await Assert.That(result.RefCount).IsEqualTo(1);
        await Assert.That(result.Capacity).IsEqualTo(5);
        await Assert.That(result.ByteLength).IsEqualTo(5);
    }

    // Sync helper — stackalloc for the 18-byte image; Latin-1 payload written as
    // compile-time span literal (H=0x48 e=0x65 l=0x6C l=0x6C o=0x6F).
    private static ZXString ParseHelloFromStub()
    {
        Span<byte> buf = stackalloc byte[ZXStringDataLayout.HeaderBytes + 6]; // 12 + 5 payload + 1 null
        BinaryPrimitives.WriteInt32LittleEndian(buf, 1); // nRef
        BinaryPrimitives.WriteInt32LittleEndian(buf[4..], 5); // nCap
        BinaryPrimitives.WriteInt32LittleEndian(buf[8..], 5); // nByteLen
        ReadOnlySpan<byte> payload = [0x48, 0x65, 0x6C, 0x6C, 0x6F]; // "Hello"
        payload.CopyTo(buf[12..]);
        return ZXString.ReadFrom(buf, payloadFileOffset: 12);
    }

    [Test]
    public async Task ZXString_ReadFrom_EmptyPayload_ReturnsEmptyString()
    {
        await Assert.That(ParseEmptyFromStub().Value).IsEqualTo(string.Empty);
    }

    // Sync helper — stackalloc for the 13-byte image (header + 1 null terminator).
    // nCap and nByteLen are left as 0 (stackalloc zeroes the buffer).
    private static ZXString ParseEmptyFromStub()
    {
        Span<byte> buf = stackalloc byte[ZXStringDataLayout.HeaderBytes + 1]; // 12 + 1 null
        BinaryPrimitives.WriteInt32LittleEndian(buf, 1); // nRef = 1
        return ZXString.ReadFrom(buf, payloadFileOffset: 12);
    }
}
