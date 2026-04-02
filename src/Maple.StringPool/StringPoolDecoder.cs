using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Maple.Native;
using Maple.StringPool.Crypto;
using Maple.StringPool.NativeTypes;
using Maple.StringPool.Source;

namespace Maple.StringPool;

/// <summary>
/// Decodes strings from the <c>StringPool</c> singleton embedded in the PE binary.
/// See <see cref="StringPoolLayout"/> for the native struct layout.
/// </summary>
/// <remarks>
/// <para>
/// Static .data members are described by <see cref="StringPoolAddresses"/>:
/// <c>ms_aString</c> (pointer table), <c>ms_aKey</c> (master key),
/// <c>ms_nKeySize</c>, <c>ms_nSize</c>. These are plain C arrays/scalars —
/// <b>not</b> <see cref="ZArrayLayout"/> heap allocations.
/// </para>
/// <para>
/// <b>Decode pipeline:</b>
/// <list type="number">
///   <item>Read the <c>char*</c> from the <c>ms_aString[index]</c> pointer table
///         (stride = <see cref="TypeSizes.Pointer"/>).</item>
///   <item>At the target, read an <see cref="EncodedEntryLayout"/>:
///         seed byte + null-terminated XOR body.</item>
///   <item>Construct a <see cref="RotatedKey"/> on the stack — backed by
///         <c>[InlineArray(256)]</c> for zero-alloc stack storage.</item>
///   <item>Decrypt via <see cref="StringPoolCrypto.Decode"/> (zero-collision XOR)
///         into a <c>stackalloc</c> buffer.</item>
///   <item>Single <see cref="string"/> allocation via
///         <c>Encoding.Latin1.GetString</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Thread safety:</b> slot access uses CAS publish-once semantics — concurrent
/// threads may decode the same slot redundantly, but only one result is stored.
/// </para>
/// </remarks>
public sealed class StringPoolDecoder : IDisposable
{
    /// <summary>
    /// Safety cap on encrypted body length to prevent unbounded <c>stackalloc</c>.
    /// No v95 string exceeds ~200 bytes; 4096 provides ample headroom.
    /// </summary>
    private const int MaxEncodedStringBytes = 4096;

    // Narrow string cache (replaces native m_apZMString). Zero-initialized, matching native ZArray alloc.
    private readonly string?[] _narrowCache;

    // Wide cache (m_apZWString) shares the narrow cache — C# strings are inherently UTF-16.
    // Lock (m_lock / ZFatalSection) is replaced by CAS below.

    // Static .data members (see StringPoolAddresses).
    private readonly int _masterKeyOffset; // ms_aKey
    private readonly int _keySize; // ms_nKeySize
    private readonly int _slotCount; // ms_nSize

    // ms_aString pointer table in the mapped image.
    private readonly int _pointerTableOffset;

    private readonly StringPoolAddresses _addresses;
    private readonly IPeImageReader _reader; // held for Dispose()
    private readonly ReadOnlyMemory<byte> _image;

    // 0 = live, 1 = disposed; volatile ensures ThrowIfDisposed() observes Dispose() writes
    // across threads without an explicit memory barrier on the read side.
    private volatile int _disposed;

    /// <summary>
    /// Constructs a <see cref="StringPoolDecoder"/> from an <see cref="IPeImageReader"/>.
    /// Reads static metadata from the .data section and pre-allocates the narrow cache.
    /// </summary>
    /// <param name="reader">Binary image source (DIP seam).</param>
    /// <param name="addresses">
    /// Static .data addresses for this binary version.
    /// Defaults to <see cref="KnownLayouts.GmsV95"/> when <see langword="null"/>.
    /// </param>
    public StringPoolDecoder(IPeImageReader reader, StringPoolAddresses? addresses = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
        _addresses = addresses ?? KnownLayouts.GmsV95;
        _image = reader.Image;

        // Bootstrap: read static members from .data segment.
        ReadOnlySpan<byte> img = _image.Span;
        _keySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(img[FileOffset(_addresses.MsNKeySize)..]);
        _slotCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(img[FileOffset(_addresses.MsNSize)..]);

        if (_keySize is <= 0 or > 256 || _slotCount is <= 0 or > 500_000)
            throw new InvalidDataException(
                $"Unexpected StringPool metadata (keySize={_keySize}, count={_slotCount}). "
                    + "Verify the binary matches the supplied StringPoolAddresses."
            );

        _masterKeyOffset = FileOffset(_addresses.MsAKey, _keySize);
        _pointerTableOffset = FileOffset(_addresses.MsAString, checked(_slotCount * TypeSizes.Pointer));

        // Allocate narrow cache to ms_nSize slots; C# arrays are zero-initialized.
        _narrowCache = new string?[_slotCount];
    }

    // ── Convenience factories ─────────────────────────────────────────────────

    /// <summary>Opens <paramref name="exePath"/> and bootstraps the pool.</summary>
    /// <param name="exePath">Path to the PE image on disk.</param>
    /// <param name="addresses">
    /// Static .data addresses for the binary version.
    /// Defaults to <see cref="KnownLayouts.GmsV95"/> when <see langword="null"/>.
    /// </param>
    /// <returns>A fully initialised <see cref="StringPoolDecoder"/> backed by <paramref name="exePath"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The binary metadata is inconsistent with <paramref name="addresses"/>.</exception>
    public static StringPoolDecoder Open(string exePath, StringPoolAddresses? addresses = null)
    {
        var reader = MemoryPeImageReader.FromFile(exePath);
        try
        {
            return new StringPoolDecoder(reader, addresses);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Wraps a pre-loaded PE image byte array.</summary>
    /// <param name="peImage">Byte array containing the full PE image.</param>
    /// <param name="addresses">
    /// Static .data addresses for the binary version.
    /// Defaults to <see cref="KnownLayouts.GmsV95"/> when <see langword="null"/>.
    /// </param>
    /// <returns>A fully initialised <see cref="StringPoolDecoder"/> backed by <paramref name="peImage"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="peImage"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The binary metadata is inconsistent with <paramref name="addresses"/>.</exception>
    public static StringPoolDecoder FromBytes(byte[] peImage, StringPoolAddresses? addresses = null)
    {
        var reader = MemoryPeImageReader.FromBytes(peImage);
        try
        {
            return new StringPoolDecoder(reader, addresses);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Wraps a pre-loaded PE image memory region without copying.</summary>
    /// <param name="peImage">Read-only memory containing the full PE image.</param>
    /// <param name="addresses">
    /// Static .data addresses for the binary version.
    /// Defaults to <see cref="KnownLayouts.GmsV95"/> when <see langword="null"/>.
    /// </param>
    /// <returns>A fully initialised <see cref="StringPoolDecoder"/> backed directly by <paramref name="peImage"/>.</returns>
    /// <exception cref="InvalidDataException">The binary metadata is inconsistent with <paramref name="addresses"/>.</exception>
    public static StringPoolDecoder FromMemory(ReadOnlyMemory<byte> peImage, StringPoolAddresses? addresses = null)
    {
        var reader = MemoryPeImageReader.FromMemory(peImage);
        try
        {
            return new StringPoolDecoder(reader, addresses);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    // ── Properties (expose static member values) ──────────────────────────────

    /// <summary>
    /// <c>StringPool::ms_nSize</c> — total string-slot count.
    /// 6883 (0x1AE3) for GMS v95.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _slotCount;
        }
    }

    /// <summary>
    /// <c>StringPool::ms_nKeySize</c>.
    /// Always 16 for v95.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public int KeySize
    {
        get
        {
            ThrowIfDisposed();
            return _keySize;
        }
    }

    /// <summary>
    /// <c>StringPool::ms_aKey</c> — the static XOR master key.
    /// Each entry's effective key is a left-rotated copy of this, keyed by the entry's seed byte.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public ReadOnlySpan<byte> MasterKey
    {
        get
        {
            ThrowIfDisposed();
            return _image.Span.Slice(_masterKeyOffset, _keySize);
        }
    }

    // ── GetString ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the decoded narrow string at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">Zero-based slot index.</param>
    /// <returns>The decoded Latin-1 string at slot <paramref name="index"/>.</returns>
    /// <remarks>
    /// Thread-safe via CAS publish-once. First access decodes and atomically
    /// stores the result; concurrent callers may decode the same slot redundantly,
    /// but only one result is retained.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is greater than or equal to <see cref="Count"/>.
    /// </exception>
    public string GetString(uint index)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, (uint)_slotCount);
        return _narrowCache[index] ?? InitializeSlot(index);
    }

    /// <summary>
    /// Wide-string variant. Returns the same value as <see cref="GetString"/> —
    /// C# strings are inherently UTF-16.
    /// </summary>
    /// <param name="index">Zero-based slot index.</param>
    /// <returns>The decoded string at slot <paramref name="index"/>.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is greater than or equal to <see cref="Count"/>.
    /// </exception>
    public string GetStringW(uint index) => GetString(index);

    /// <summary>BSTR wrapper variant. Delegates to <see cref="GetString"/>.</summary>
    /// <param name="index">Zero-based slot index.</param>
    /// <returns>The decoded string at slot <paramref name="index"/>.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is greater than or equal to <see cref="Count"/>.
    /// </exception>
    public string GetBSTR(uint index) => GetString(index);

    // ── Enumeration ───────────────────────────────────────────────────────────

    /// <summary>Returns a stable snapshot of all entries in ascending index order.</summary>
    /// <returns>A snapshot of all <see cref="StringPoolEntry"/> values, index 0 to <see cref="Count"/> − 1.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public IEnumerable<StringPoolEntry> GetAll()
    {
        ThrowIfDisposed();
        return GetRangeSnapshot(0, (uint)_slotCount);
    }

    /// <summary>
    /// Enumerates entries in [<paramref name="start"/>, <paramref name="end"/>).
    /// </summary>
    /// <param name="start">Inclusive lower bound (zero-based slot index).</param>
    /// <param name="end">
    /// Exclusive upper bound. Silently clamped to <see cref="Count"/> when it exceeds
    /// the total slot count; pass <see cref="uint.MaxValue"/> to mean "to the last slot".
    /// </param>
    /// <returns>A stable snapshot of entries whose index satisfies <c>start ≤ index &lt; end</c>.</returns>
    /// <remarks>
    /// If <paramref name="start"/> is at or beyond the slot count after clamping
    /// is applied, an empty sequence is returned without throwing.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="start"/> is greater than <paramref name="end"/>.</exception>
    public IEnumerable<StringPoolEntry> GetRange(uint start, uint end)
    {
        ThrowIfDisposed();
        if (start > end)
            throw new ArgumentException($"start ({start}) must be <= end ({end}).", nameof(start));
        return GetRangeSnapshot(start, end);
    }

    /// <summary>
    /// Enumerates all entries without first materialising a snapshot array.
    /// </summary>
    /// <returns>A live view of all <see cref="StringPoolEntry"/> values in ascending index order.</returns>
    /// <remarks>
    /// This path avoids the snapshot array allocated by <see cref="GetAll"/>, but the
    /// decoder must remain undisposed for the full enumeration.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public IEnumerable<StringPoolEntry> EnumerateAll()
    {
        ThrowIfDisposed();
        return EnumerateRangeIterator(this, 0, (uint)_slotCount);
    }

    /// <summary>
    /// Enumerates entries in [<paramref name="start"/>, <paramref name="end"/>) without
    /// first materialising a snapshot array.
    /// </summary>
    /// <param name="start">Inclusive lower bound (zero-based slot index).</param>
    /// <param name="end">
    /// Exclusive upper bound. Silently clamped to <see cref="Count"/> when it exceeds
    /// the total slot count; pass <see cref="uint.MaxValue"/> to mean "to the last slot".
    /// </param>
    /// <returns>A live view of entries whose index satisfies <c>start ≤ index &lt; end</c>.</returns>
    /// <remarks>
    /// This path avoids the snapshot array allocated by <see cref="GetRange"/>, but the
    /// decoder must remain undisposed for the full enumeration.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentException"><paramref name="start"/> is greater than <paramref name="end"/>.</exception>
    public IEnumerable<StringPoolEntry> EnumerateRange(uint start, uint end)
    {
        ThrowIfDisposed();
        if (start > end)
            throw new ArgumentException($"start ({start}) must be <= end ({end}).", nameof(start));
        return EnumerateRangeIterator(this, start, end);
    }

    private StringPoolEntry[] GetRangeSnapshot(uint start, uint end)
    {
        uint clampedEnd = Math.Min(end, (uint)_slotCount);
        if (start >= clampedEnd)
            return [];

        var entries = new StringPoolEntry[checked((int)(clampedEnd - start))];
        for (uint i = start; i < clampedEnd; i++)
            entries[i - start] = new StringPoolEntry(i, GetString(i));

        return entries;
    }

    private static IEnumerable<StringPoolEntry> EnumerateRangeIterator(StringPoolDecoder decoder, uint start, uint end)
    {
        decoder.ThrowIfDisposed();

        uint clampedEnd = Math.Min(end, (uint)decoder._slotCount);
        for (uint i = start; i < clampedEnd; i++)
            yield return new StringPoolEntry(i, decoder.GetString(i));
    }

    /// <summary>
    /// Searches all entries for <paramref name="term"/>.
    /// </summary>
    /// <param name="term">The substring to search for.</param>
    /// <param name="comparison">String comparison mode. Defaults to <see cref="StringComparison.OrdinalIgnoreCase"/>.</param>
    /// <returns>A stable snapshot of entries whose <see cref="StringPoolEntry.Value"/> contains <paramref name="term"/>.</returns>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <see langword="null"/>.</exception>
    public IEnumerable<StringPoolEntry> Find(
        string term,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase
    )
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(term);
        return FindSnapshot(term, comparison);
    }

    /// <summary>
    /// Searches all entries for <paramref name="term"/> without first materialising a snapshot.
    /// </summary>
    /// <param name="term">The substring to search for.</param>
    /// <param name="comparison">String comparison mode. Defaults to <see cref="StringComparison.OrdinalIgnoreCase"/>.</param>
    /// <returns>A live view of entries whose <see cref="StringPoolEntry.Value"/> contains <paramref name="term"/>.</returns>
    /// <remarks>
    /// This path avoids the snapshot allocations made by <see cref="Find"/>, but the decoder
    /// must remain undisposed for the full enumeration.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <see langword="null"/>.</exception>
    public IEnumerable<StringPoolEntry> EnumerateFind(
        string term,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase
    )
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(term);
        return EnumerateFindIterator(this, term, comparison);
    }

    private List<StringPoolEntry> FindSnapshot(string term, StringComparison comparison)
    {
        List<StringPoolEntry> matches = [];
        for (uint i = 0; i < (uint)_slotCount; i++)
        {
            string value = GetString(i);
            if (value.Contains(term, comparison))
                matches.Add(new StringPoolEntry(i, value));
        }

        return matches;
    }

    private static IEnumerable<StringPoolEntry> EnumerateFindIterator(
        StringPoolDecoder decoder,
        string term,
        StringComparison comparison
    )
    {
        decoder.ThrowIfDisposed();

        for (uint i = 0; i < (uint)decoder._slotCount; i++)
        {
            string value = decoder.GetString(i);
            if (value.Contains(term, comparison))
                yield return new StringPoolEntry(i, value);
        }
    }

    // ── Decode pipeline ───────────────────────────────────────────────────────

    /// <summary>
    /// Decodes a slot and publishes it via CAS — only one result wins.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private string InitializeSlot(uint index)
    {
        string decoded = DecodeSlot(index);
        // Return the winner: if CAS succeeds, prior value was null so we return decoded;
        // if another thread won the race, the prior (non-null) stored value is returned.
        return Interlocked.CompareExchange(ref _narrowCache[index], decoded, comparand: null) ?? decoded;
    }

    /// <summary>Core decode path for a single slot.</summary>
    /// <remarks>
    /// <para>
    /// Step 1: Index the pre-cached <c>ms_aString</c> pointer table.
    /// </para>
    /// <para>
    /// Step 2: At the target, read the <see cref="EncodedEntryLayout"/>:
    /// <c>byte[0]</c> = seed (signed rotation amount),
    /// <c>byte[1..]</c> = null-terminated XOR-encrypted body.
    /// </para>
    /// <para>
    /// Step 3: Construct a <see cref="RotatedKey"/> on the stack via
    /// <c>[InlineArray(256)]</c> — zero heap allocations.
    /// </para>
    /// <para>
    /// Step 4: Decrypt via <see cref="StringPoolCrypto.Decode"/> (zero-collision XOR rule).
    /// </para>
    /// </remarks>
    private string DecodeSlot(uint index)
    {
        ReadOnlySpan<byte> img = _image.Span;

        // Step 1: Index the ms_aString pointer table directly in the image.
        uint entryPointer = ReadPointerTableEntry(index);

        // Null or out-of-range pointers → empty string (mirrors runtime null-guard).
        if (entryPointer < _addresses.ImageBase)
            return string.Empty;

        int entryOff = (int)(entryPointer - _addresses.ImageBase);
        if ((uint)entryOff >= (uint)img.Length)
            return string.Empty;

        // Step 2: EncodedEntryLayout — [0]=seed, [1..null]=XOR-encrypted body.
        int seed = (sbyte)img[entryOff + EncodedEntryLayout.SeedOffset];

        // Slice directly into the XOR-encrypted body.
        ReadOnlySpan<byte> bodySpan = img[(entryOff + EncodedEntryLayout.BodyOffset)..];
        int nullAt = bodySpan.IndexOf((byte)0);
        int encryptedLength = Math.Min(nullAt < 0 ? bodySpan.Length : nullAt, MaxEncodedStringBytes);

        if (encryptedLength == 0)
            return string.Empty;

        // Step 3: Construct a RotatedKey on the stack — InlineArray, zero heap alloc.
        RotatedKey rotatedKey = new(img.Slice(_masterKeyOffset, _keySize), seed);

        // Step 4: Decrypt into a plain-text stackalloc buffer.
        Span<byte> plainBuffer = stackalloc byte[encryptedLength];
        StringPoolCrypto.Decode(bodySpan[..encryptedLength], in rotatedKey, plainBuffer);

        return Encoding.Latin1.GetString(plainBuffer);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts an absolute memory address to a file offset relative to the image base.
    /// </summary>
    /// <remarks>
    /// Assumes a flat PE mapping where RVA equals file offset — valid for GMS v95 because
    /// the executable's section file-alignment matches its virtual-address alignment.
    /// </remarks>
    private int FileOffset(uint memoryAddress)
    {
        if (memoryAddress < _addresses.ImageBase)
            throw new InvalidDataException(
                $"Address 0x{memoryAddress:X} is below image base 0x{_addresses.ImageBase:X}."
            );

        int offset = (int)(memoryAddress - _addresses.ImageBase);
        if (offset >= _image.Length)
            throw new InvalidDataException(
                $"Address 0x{memoryAddress:X} (offset 0x{offset:X}) is beyond the end of the image (size 0x{_image.Length:X}). "
                    + "Verify the binary matches the supplied StringPoolAddresses."
            );

        return offset;
    }

    private int FileOffset(uint memoryAddress, int readableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(readableBytes);

        int offset = FileOffset(memoryAddress);
        if (offset > _image.Length - readableBytes)
        {
            throw new InvalidDataException(
                $"Address 0x{memoryAddress:X} (offset 0x{offset:X}) with length 0x{readableBytes:X} exceeds the end of the image (size 0x{_image.Length:X}). "
                    + "Verify the binary matches the supplied StringPoolAddresses."
            );
        }

        return offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadPointerTableEntry(uint index)
    {
        int pointerOffset = _pointerTableOffset + checked((int)index * TypeSizes.Pointer);
        return BinaryPrimitives.ReadUInt32LittleEndian(_image.Span[pointerOffset..]);
    }

    [System.Diagnostics.StackTraceHidden]
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _reader.Dispose();
    }
}
