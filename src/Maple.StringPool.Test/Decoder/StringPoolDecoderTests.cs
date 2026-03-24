using System.Text;
using Maple.StringPool.NativeTypes;
using Maple.StringPool.Test.Helpers;

namespace Maple.StringPool.Test.Decoder;

/// <summary>
/// Demonstrates <see cref="StringPoolDecoder"/> against a synthetic in-memory
/// PE image that faithfully replicates the GMS v95 binary layout.
/// </summary>
/// <remarks>
/// <para>
/// String-pool slot→value examples used throughout these tests are taken
/// directly from the official <c>strings_v95.json</c> reference — the same
/// values the real MapleStory.exe binary produces at runtime:
/// </para>
/// <code>
///   Slot   8  →  "Tahoma"
///   Slot  12  →  "Beginner"
///   Slot  13  →  "Warrior"
///   Slot  25  →  "Hero"
/// </code>
/// <para>
/// The stub image is built with a deterministic synthetic master key. Encoded
/// byte vectors are pre-computed so tests are self-contained and require no
/// real binary.
/// </para>
/// </remarks>
public sealed class StringPoolDecoderTests
{
    // ── Test constants ────────────────────────────────────────────────────────

    // Synthetic 16-byte master key — same length as the real ms_aKey (0xB98830).
    // Computed test vectors below use seed=0 (no rotation), so the rotated key
    // equals this master key directly.
    private static readonly byte[] TestMasterKey =
    [
        0x6C,
        0xEB,
        0xBC,
        0x5E,
        0x4B,
        0xCC,
        0x47,
        0x5D,
        0x3E,
        0x3B,
        0xC2,
        0x90,
        0x82,
        0xDA,
        0x69,
        0x83,
    ];

    // File offsets (= VA – 0x400000) of the static data section fields.
    private const long OffMsNKeySize = 0xB98840 - 0x400000; // 0x798840
    private const long OffMsNSize = 0xB98844 - 0x400000; // 0x798844
    private const long OffMsAKey = 0xB98830 - 0x400000; // 0x798830
    private const long OffMsAString = 0xC5A878 - 0x400000; // 0x85A878

    // File offset base for synthetic encoded-entry blobs.
    private const long EntryBlobBase = 0x820000;
    private const long BlobStride = 0x100; // 256 bytes per slot — generous padding

    // Slot–string pairs from strings_v95.json used as doc examples.
    private static readonly (uint Slot, string Plain, byte[] EncBody)[] KnownEntries =
    [
        // Slot  8 : "Tahoma"   — font name used by the client UI
        (8, "Tahoma", [0x38, 0x8A, 0xD4, 0x31, 0x26, 0xAD]),
        // Slot 12 : "Beginner" — first job class (no advancement yet)
        (12, "Beginner", [0x2E, 0x8E, 0xDB, 0x37, 0x25, 0xA2, 0x22, 0x2F]),
        // Slot 13 : "Warrior"  — 1st job: Swordman branch
        (13, "Warrior", [0x3B, 0x8A, 0xCE, 0x2C, 0x22, 0xA3, 0x35]),
        // Slot 25 : "Hero"     — 4th job advancement of Fighter
        (25, "Hero", [0x24, 0x8E, 0xCE, 0x31]),
    ];

    // Required slot count must cover the highest slot index used (25 + 1 = 26).
    private const int StubSlotCount = 100;

    // ── Fixture factory ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="StubPeImageReader"/> that models the exact memory
    /// layout <see cref="StringPoolDecoder"/> reads during bootstrap and decode:
    /// <list type="bullet">
    ///   <item>Static metadata: <c>ms_nKeySize</c>, <c>ms_nSize</c>, <c>ms_aKey</c>.</item>
    ///   <item>Per-entry layout: <c>[seed, XOR-body..., 0x00]</c> at resolved file offsets.</item>
    ///   <item>Pointer table: <c>ms_aString[slot] = VA of entry blob</c>.</item>
    /// </list>
    /// </summary>
    private static StubPeImageReader BuildStubImage()
    {
        var reader = new StubPeImageReader();

        // 1. Bootstrap metadata (mirrors StringPool::StringPool() at 0x7465D0).
        reader.WriteUInt32At(OffMsNKeySize, (uint)TestMasterKey.Length);
        reader.WriteUInt32At(OffMsNSize, (uint)StubSlotCount);
        reader.WriteAt(OffMsAKey, TestMasterKey);

        // 2. Place each encoded entry and write its VA into the pointer table.
        foreach ((uint slot, _, byte[] encBody) in KnownEntries)
        {
            long entryFileOffset = EntryBlobBase + (slot * BlobStride);

            // Encoded entry format: [seed(1), body(n), null(1)]
            // seed=0 means the per-entry key equals ms_aKey directly.
            byte[] entry = new byte[1 + encBody.Length + 1];
            entry[0] = 0x00; // seed byte
            Buffer.BlockCopy(encBody, 0, entry, 1, encBody.Length);
            entry[^1] = 0x00; // null terminator
            reader.WriteAt(entryFileOffset, entry);

            // Virtual address of this entry = imageBase + fileOffset.
            uint entryVa = (uint)(KnownLayouts.GmsV95.ImageBase + entryFileOffset);
            reader.WriteUInt32At(OffMsAString + slot * TypeSizes.Pointer, entryVa);
        }

        return reader;
    }

    // ── Bootstrap tests ───────────────────────────────────────────────────────

    [Test]
    public async Task Count_ReturnsSlotCountFromStaticMetadata()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.Count).IsEqualTo(StubSlotCount);
    }

    [Test]
    public async Task KeySize_Returns16_MatchingMasterKeyLength()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.KeySize).IsEqualTo(16);
    }

    [Test]
    public async Task MasterKey_MatchesRegisteredBytes()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(Convert.ToHexString(pool.MasterKey)).IsEqualTo(Convert.ToHexString(TestMasterKey));
    }

    // ── Decode examples from strings_v95.json ────────────────────────────────

    [Test]
    public async Task GetString_Slot8_ReturnsTahoma()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.GetString(8)).IsEqualTo("Tahoma");
    }

    [Test]
    public async Task GetString_Slot12_ReturnsBeginner()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.GetString(12)).IsEqualTo("Beginner");
    }

    [Test]
    public async Task GetString_Slot13_ReturnsWarrior()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.GetString(13)).IsEqualTo("Warrior");
    }

    [Test]
    public async Task GetString_Slot25_ReturnsHero()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.GetString(25)).IsEqualTo("Hero");
    }

    // Wide and BSTR variants delegate to the same decoded string.
    [Test]
    public async Task GetStringW_And_GetBSTR_ReturnSameValueAsGetString()
    {
        using StringPoolDecoder pool = new(BuildStubImage());
        string narrow = pool.GetString(8);

        await Assert.That(pool.GetStringW(8)).IsEqualTo(narrow);
        await Assert.That(pool.GetBSTR(8)).IsEqualTo(narrow);
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Test]
    public async Task GetString_CalledTwice_ReturnsCachedInstance()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        string first = pool.GetString(8);
        string second = pool.GetString(8);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    // ── Null / empty entries ──────────────────────────────────────────────────

    [Test]
    public async Task GetString_UnregisteredSlot_ReturnsEmptyString()
    {
        // Slot 0 has no entry in the stub → pointer table reads 0x00000000
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert.That(pool.GetString(0)).IsEqualTo(string.Empty);
    }

    // ── Bounds ────────────────────────────────────────────────────────────────

    [Test]
    public async Task GetString_IndexOutOfRange_Throws()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert
            .That(() =>
            {
                _ = pool.GetString((uint)StubSlotCount);
            })
            .Throws<ArgumentOutOfRangeException>();
    }

    // ── Enumeration ───────────────────────────────────────────────────────────

    [Test]
    public async Task GetAll_ContainsAllKnownEntries()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        StringPoolEntry[] all = pool.GetAll().ToArray();

        foreach ((uint slot, string plain, _) in KnownEntries)
        {
            StringPoolEntry entry = all.Single(e => e.Index == slot);
            await Assert.That(entry.Value).IsEqualTo(plain);
        }
    }

    [Test]
    public async Task Find_ByJobName_ReturnsMatchingEntry()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        StringPoolEntry[] hits = pool.Find("warrior").ToArray();

        await Assert.That(hits.Length).IsGreaterThan(0);
        await Assert.That(hits[0].Index).IsEqualTo(13u);
        await Assert.That(hits[0].Value).IsEqualTo("Warrior");
    }

    [Test]
    public async Task GetRange_StartGreaterThanEnd_ThrowsEagerly()
    {
        // Validation must throw before any iteration (eager, not deferred).
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert
            .That(() =>
            {
                _ = pool.GetRange(10, 5);
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Find_NullTerm_ThrowsEagerly()
    {
        // Null check must fire before iteration.
        using StringPoolDecoder pool = new(BuildStubImage());

        await Assert
            .That(() =>
            {
                _ = pool.Find(null!);
            })
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StringPoolEntry_ToString_FormatsCorrectly()
    {
        var entry = new StringPoolEntry(25, "Hero");

        await Assert.That(entry.ToString()).IsEqualTo("SP[0x19] (25): Hero");
    }

    // ── Non-zero seed rotation coverage ──────────────────────────────────────

    [Test]
    public async Task GetString_PositiveSeed8_DecodesCorrectly()
    {
        // seed = 8: element-rotate left by 1 (8>>3=1), no bit-rotation (8&7=0).
        // Rotated key[0..3] = [0xEB, 0xBC, 0x5E, 0x4B].
        // "Pass" = [0x50, 0x61, 0x73, 0x73] → XOR → [0xBB, 0xDD, 0x2D, 0x38].
        const byte seed = 8;
        const uint slot = 50;
        const string expected = "Pass";
        byte[] encBody = [0xBB, 0xDD, 0x2D, 0x38];

        StubPeImageReader reader = BuildStubImageWithExtraEntry(slot, seed, encBody);

        using StringPoolDecoder pool = new(reader);
        await Assert.That(pool.GetString(slot)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetString_NegativeSeed_DecodesCorrectly()
    {
        // seed = 0xFF → (sbyte) = -1.
        // element-rotate left by 15 (right by 1), bit-rotate left by 7.
        // Rotated key[0..1] = [0xB6, 0x75].
        // "Ax" = [0x41, 0x78] → XOR → [0xF7, 0x0D].
        const byte seed = 0xFF; // interpreted as sbyte -1 by the decoder
        const uint slot = 60;
        const string expected = "Ax";
        byte[] encBody = [0xF7, 0x0D];

        StubPeImageReader reader = BuildStubImageWithExtraEntry(slot, seed, encBody);

        using StringPoolDecoder pool = new(reader);
        await Assert.That(pool.GetString(slot)).IsEqualTo(expected);
    }

    /// <summary>
    /// Builds a stub image pre-populated with all <see cref="KnownEntries"/> (seed=0)
    /// and one additional synthetic entry at <paramref name="slot"/> with the given
    /// <paramref name="seed"/> and <paramref name="encBody"/>.
    /// </summary>
    private static StubPeImageReader BuildStubImageWithExtraEntry(uint slot, byte seed, byte[] encBody)
    {
        StubPeImageReader reader = BuildStubImage();

        long entryFileOffset = EntryBlobBase + (slot * BlobStride);
        byte[] entry = new byte[1 + encBody.Length + 1]; // seed + body + null
        entry[0] = seed;
        Buffer.BlockCopy(encBody, 0, entry, 1, encBody.Length);
        reader.WriteAt(entryFileOffset, entry);

        uint entryVa = (uint)(KnownLayouts.GmsV95.ImageBase + entryFileOffset);
        reader.WriteUInt32At(OffMsAString + (slot * TypeSizes.Pointer), entryVa);

        return reader;
    }

    // ── Constructor: null reader ──────────────────────────────────────────────

    [Test]
    public async Task Constructor_NullReader_ThrowsArgumentNullException()
    {
        await Assert
            .That(() =>
            {
                _ = new StringPoolDecoder(null!);
            })
            .Throws<ArgumentNullException>();
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Test]
    public async Task Constructor_ZeroKeySize_ThrowsInvalidDataException()
    {
        StubPeImageReader reader = new();
        reader.WriteUInt32At(OffMsNKeySize, 0);
        reader.WriteUInt32At(OffMsNSize, StubSlotCount);

        await Assert
            .That(() =>
            {
                _ = new StringPoolDecoder(reader);
            })
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Constructor_OversizedKeySize_ThrowsInvalidDataException()
    {
        StubPeImageReader reader = new();
        reader.WriteUInt32At(OffMsNKeySize, 257); // > 256
        reader.WriteUInt32At(OffMsNSize, StubSlotCount);

        await Assert
            .That(() =>
            {
                _ = new StringPoolDecoder(reader);
            })
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Constructor_ZeroSlotCount_ThrowsInvalidDataException()
    {
        StubPeImageReader reader = new();
        reader.WriteUInt32At(OffMsNKeySize, (uint)TestMasterKey.Length);
        reader.WriteUInt32At(OffMsNSize, 0);

        await Assert
            .That(() =>
            {
                _ = new StringPoolDecoder(reader);
            })
            .Throws<InvalidDataException>();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Dispose_ThenGetString_ThrowsObjectDisposedException()
    {
        StringPoolDecoder pool = new(BuildStubImage());
        pool.Dispose();

        await Assert
            .That(() =>
            {
                _ = pool.GetString(8);
            })
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Dispose_ThenGetAll_ThrowsEagerly()
    {
        StringPoolDecoder pool = new(BuildStubImage());
        pool.Dispose();

        // Must throw before returning IEnumerable — not deferred.
        await Assert
            .That(() =>
            {
                _ = pool.GetAll();
            })
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Dispose_ThenGetRange_ThrowsEagerly()
    {
        StringPoolDecoder pool = new(BuildStubImage());
        pool.Dispose();

        await Assert
            .That(() =>
            {
                _ = pool.GetRange(0, 10);
            })
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Dispose_ThenFind_ThrowsEagerly()
    {
        StringPoolDecoder pool = new(BuildStubImage());
        pool.Dispose();

        await Assert
            .That(() =>
            {
                _ = pool.Find("Warrior");
            })
            .Throws<ObjectDisposedException>();
    }

    // ── GetRange end-clamping ─────────────────────────────────────────────────

    [Test]
    public async Task GetRange_EndBeyondCount_ClampsToBoundary()
    {
        using StringPoolDecoder pool = new(BuildStubImage());

        // StubSlotCount is 100; pass 200 as end — should clamp silently to 100 entries.
        StringPoolEntry[] entries = pool.GetRange(0, 200).ToArray();

        await Assert.That(entries.Length).IsEqualTo(StubSlotCount);
        await Assert.That(entries[^1].Index).IsEqualTo((uint)(StubSlotCount - 1));
    }

    // ── Key-wrapping (string longer than key length) ──────────────────────────

    [Test]
    public async Task GetString_LongString_WrapsKeyAtIndex16()
    {
        // "Hello, MapleStory" is 17 bytes — byte 16 uses key[16 % 16] = key[0].
        // Encoded using seed=0 and TestMasterKey; verified byte-by-byte:
        //   plain[16]='y'=0x79, key[0]=0x6C → enc=0x79^0x6C=0x15.
        const uint slot = 70;
        const string expected = "Hello, MapleStory";
        byte[] encBody =
        [
            0x24,
            0x8E,
            0xD0,
            0x32,
            0x24,
            0xE0,
            0x67,
            0x10,
            0x5F,
            0x4B,
            0xAE,
            0xF5,
            0xD1,
            0xAE,
            0x06,
            0xF1,
            0x15,
        ];

        StubPeImageReader reader = BuildStubImageWithExtraEntry(slot, seed: 0, encBody);

        using StringPoolDecoder pool = new(reader);
        await Assert.That(pool.GetString(slot)).IsEqualTo(expected);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Factory methods: Open / FromBytes
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="StringPoolDecoder.Open"/> and
/// <see cref="StringPoolDecoder.FromBytes"/> convenience factories.
/// </summary>
public sealed class StringPoolDecoderFactoryTests
{
    // ── Shared helpers (duplicated from the main fixture for isolation) ────────

    private static readonly byte[] TestMasterKey =
    [
        0x6C,
        0xEB,
        0xBC,
        0x5E,
        0x4B,
        0xCC,
        0x47,
        0x5D,
        0x3E,
        0x3B,
        0xC2,
        0x90,
        0x82,
        0xDA,
        0x69,
        0x83,
    ];

    private const long OffMsNKeySize = 0xB98840 - 0x400000;
    private const long OffMsNSize = 0xB98844 - 0x400000;
    private const long OffMsAKey = 0xB98830 - 0x400000;
    private const long OffMsAString = 0xC5A878 - 0x400000;
    private const long EntryBlobBase = 0x820000;
    private const int StubSlotCount = 100;

    // Slot 8 → "Tahoma" (seed=0, same TestMasterKey)
    private static readonly byte[] Slot8EncBody = [0x38, 0x8A, 0xD4, 0x31, 0x26, 0xAD];

    private static StubPeImageReader BuildStubImage()
    {
        var reader = new StubPeImageReader();
        reader.WriteUInt32At(OffMsNKeySize, (uint)TestMasterKey.Length);
        reader.WriteUInt32At(OffMsNSize, (uint)StubSlotCount);
        reader.WriteAt(OffMsAKey, TestMasterKey);

        long entryOffset = EntryBlobBase + (8 * 0x100);
        byte[] entry = new byte[1 + Slot8EncBody.Length + 1];
        entry[0] = 0x00;
        Buffer.BlockCopy(Slot8EncBody, 0, entry, 1, Slot8EncBody.Length);
        reader.WriteAt(entryOffset, entry);

        uint entryVa = (uint)(KnownLayouts.GmsV95.ImageBase + entryOffset);
        reader.WriteUInt32At(OffMsAString + 8 * TypeSizes.Pointer, entryVa);

        return reader;
    }

    // ── StringPoolDecoder.Open ────────────────────────────────────────────────

    [Test]
    public async Task Open_NullPath_ThrowsArgumentNullException()
    {
        await Assert.That(() => StringPoolDecoder.Open(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Open_NonExistentFile_ThrowsFileNotFoundException()
    {
        string path = Path.Combine(Path.GetTempPath(), "maple_test_no_such_file_4d5e6f.exe");

        await Assert.That(() => StringPoolDecoder.Open(path)).Throws<FileNotFoundException>();
    }

    [Test]
    public async Task Open_FileWithInvalidMeta_DisposesReaderAndRethrows()
    {
        // A tiny (256-byte) file is too small for GmsV95 addresses →
        // FileOffset throws InvalidDataException → catch block disposes reader and re-throws.
        string tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpFile, new byte[256]);

            await Assert.That(() => StringPoolDecoder.Open(tmpFile)).Throws<InvalidDataException>();
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    [Test]
    public async Task Open_ValidFile_DecodesExpectedSlot()
    {
        // Build the stub byte array, write it to a temp file, then decode via Open.
        byte[] imageBytes = BuildStubImage().Image.ToArray();
        string tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpFile, imageBytes);

            using StringPoolDecoder pool = StringPoolDecoder.Open(tmpFile);

            await Assert.That(pool.GetString(8)).IsEqualTo("Tahoma");
            await Assert.That(pool.Count).IsEqualTo(StubSlotCount);
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    // ── StringPoolDecoder.FromBytes ───────────────────────────────────────────

    [Test]
    public async Task FromBytes_NullArgument_ThrowsArgumentNullException()
    {
        await Assert.That(() => StringPoolDecoder.FromBytes(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task FromBytes_InvalidMeta_DisposesReaderAndRethrows()
    {
        // 256-byte zeroed array → FileOffset throws (image too small for GmsV95 addresses).
        await Assert.That(() => StringPoolDecoder.FromBytes(new byte[256])).Throws<InvalidDataException>();
    }

    [Test]
    public async Task FromBytes_ValidImage_DecodesExpectedSlot()
    {
        byte[] imageBytes = BuildStubImage().Image.ToArray();

        using StringPoolDecoder pool = StringPoolDecoder.FromBytes(imageBytes);

        await Assert.That(pool.GetString(8)).IsEqualTo("Tahoma");
        await Assert.That(pool.Count).IsEqualTo(StubSlotCount);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// FileOffset error paths and DecodeSlot edge cases
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <c>FileOffset</c> throw paths and <c>DecodeSlot</c> guard branches
/// (entry pointer below image base; entry offset beyond image end).
/// </summary>
public sealed class StringPoolDecoderEdgeCaseTests
{
    private const long OffMsNKeySize = 0xB98840 - 0x400000;
    private const long OffMsNSize = 0xB98844 - 0x400000;
    private const long OffMsAKey = 0xB98830 - 0x400000;
    private const long OffMsAString = 0xC5A878 - 0x400000;
    private const int StubSlotCount = 100;

    private static readonly byte[] TestMasterKey =
    [
        0x6C,
        0xEB,
        0xBC,
        0x5E,
        0x4B,
        0xCC,
        0x47,
        0x5D,
        0x3E,
        0x3B,
        0xC2,
        0x90,
        0x82,
        0xDA,
        0x69,
        0x83,
    ];

    private static StubPeImageReader BuildMinimalStub()
    {
        var reader = new StubPeImageReader();
        reader.WriteUInt32At(OffMsNKeySize, (uint)TestMasterKey.Length);
        reader.WriteUInt32At(OffMsNSize, (uint)StubSlotCount);
        reader.WriteAt(OffMsAKey, TestMasterKey);
        // pointer table left all-zero → every slot has null-pointer VA
        return reader;
    }

    // ── FileOffset — address below image base ─────────────────────────────────

    [Test]
    public async Task Constructor_MsNKeySizeAddressBelowImageBase_ThrowsInvalidDataException()
    {
        // ImageBase = 0x400000; MsNKeySize = 0x1 (< ImageBase) → FileOffset throws.
        var addresses = new StringPoolAddresses
        {
            ImageBase = 0x400000u,
            MsNKeySize = 0x000001u,
            MsNSize = KnownLayouts.GmsV95.MsNSize,
            MsAKey = KnownLayouts.GmsV95.MsAKey,
            MsAString = KnownLayouts.GmsV95.MsAString,
        };

        await Assert
            .That(() => new StringPoolDecoder(new StubPeImageReader(), addresses))
            .Throws<InvalidDataException>();
    }

    // ── FileOffset — offset beyond image end ──────────────────────────────────

    [Test]
    public async Task Constructor_MsNKeySizeAddressBeyondImage_ThrowsInvalidDataException()
    {
        // StubPeImageReader is 0x900000 bytes; offset = 0xD00001 - 0x400000 = 0x900001 ≥ length.
        var addresses = new StringPoolAddresses
        {
            ImageBase = 0x400000u,
            MsNKeySize = 0xD00001u,
            MsNSize = KnownLayouts.GmsV95.MsNSize,
            MsAKey = KnownLayouts.GmsV95.MsAKey,
            MsAString = KnownLayouts.GmsV95.MsAString,
        };

        await Assert
            .That(() => new StringPoolDecoder(new StubPeImageReader(), addresses))
            .Throws<InvalidDataException>();
    }

    // ── DecodeSlot — entry pointer below image base ───────────────────────────

    [Test]
    public async Task GetString_EntryPointerBelowImageBase_ReturnsEmptyString()
    {
        StubPeImageReader reader = BuildMinimalStub();

        // Write a VA below image base (0x400000) into slot 1's pointer table entry.
        // The pointer table is pre-read at construction, so this must be set before new().
        reader.WriteUInt32At(OffMsAString + 1 * TypeSizes.Pointer, 0x100000u);

        using StringPoolDecoder pool = new(reader);

        await Assert.That(pool.GetString(1)).IsEqualTo(string.Empty);
    }

    // ── DecodeSlot — entry offset beyond image end ────────────────────────────

    [Test]
    public async Task GetString_EntryOffsetBeyondImage_ReturnsEmptyString()
    {
        StubPeImageReader reader = BuildMinimalStub();

        // VA 0xD00001 → offset 0x900001 ≥ StubPeImageReader buffer (0x900000 bytes).
        reader.WriteUInt32At(OffMsAString + 2 * TypeSizes.Pointer, 0xD00001u);

        using StringPoolDecoder pool = new(reader);

        await Assert.That(pool.GetString(2)).IsEqualTo(string.Empty);
    }

    // ── Dispose — double dispose ──────────────────────────────────────────────

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        StringPoolDecoder pool = new(BuildMinimalStub());
        pool.Dispose();
        pool.Dispose(); // Interlocked.Exchange sees 1 → returns immediately

        // If no exception reaches here the test passes.
        await Assert.That(pool.GetType()).IsNotNull();
    }

    // ── Properties — throw after dispose ─────────────────────────────────────

    [Test]
    public async Task Count_AfterDispose_ThrowsObjectDisposedException()
    {
        StringPoolDecoder pool = new(BuildMinimalStub());
        pool.Dispose();

        await Assert.That(() => _ = pool.Count).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task KeySize_AfterDispose_ThrowsObjectDisposedException()
    {
        StringPoolDecoder pool = new(BuildMinimalStub());
        pool.Dispose();

        await Assert.That(() => _ = pool.KeySize).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task MasterKey_AfterDispose_ThrowsObjectDisposedException()
    {
        StringPoolDecoder pool = new(BuildMinimalStub());
        pool.Dispose();

        await Assert.That(() => _ = pool.MasterKey.Length).Throws<ObjectDisposedException>();
    }

    // ── GetRange — start equals end ───────────────────────────────────────────

    [Test]
    public async Task GetRange_StartEqualsEnd_ReturnsEmptySequence()
    {
        using StringPoolDecoder pool = new(BuildMinimalStub());

        StringPoolEntry[] entries = pool.GetRange(5, 5).ToArray();

        await Assert.That(entries.Length).IsEqualTo(0);
    }
}
