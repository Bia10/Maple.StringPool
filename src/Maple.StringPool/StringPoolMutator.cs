using System.Buffers;
using System.Buffers.Binary;
using Maple.Native;
using Maple.StringPool.Crypto;
using Maple.StringPool.NativeTypes;

namespace Maple.StringPool;

/// <summary>
/// Addresses of one runtime StringPool slot substitution.
/// </summary>
public readonly record struct StringPoolRuntimeSubstitution(
    uint EncodedEntryAddress,
    uint NarrowStringAddress,
    uint WideStringAddress
);

/// <summary>
/// Writes replacement StringPool entries into native memory for live-client mutation scenarios.
/// </summary>
/// <remarks>
/// <para>
/// The Maple v95 client does not expose a growable StringPool table. The static
/// <c>ms_aString</c> member is a fixed <c>const char*[N]</c> array in <c>.data</c>, and
/// <c>ms_nSize</c> is a fixed scalar. In practice this means "adding" a string requires
/// reusing an existing slot unless the caller also patches client code that depends on the
/// original table size and location.
/// </para>
/// <para>
/// This helper therefore supports two realistic mutation paths:
/// </para>
/// <list type="number">
///   <item>Replace a static <c>ms_aString[index]</c> pointer with a newly encoded entry.</item>
///   <item>Inject a ready-made <c>ZXString</c> into the live narrow or wide cache for immediate runtime use.</item>
/// </list>
/// </remarks>
public static class StringPoolMutator
{
    private const int StackBufferBytes = 256;

    /// <summary>
    /// Encodes one native StringPool entry as <c>[seed][xor-body][0]</c>.
    /// </summary>
    public static byte[] EncodeEntry(string value, ReadOnlySpan<byte> masterKey, sbyte seed = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateMasterKey(masterKey);

        int payloadLength = value.Length;
        byte[] entry = new byte[EncodedEntryLayout.BodyOffset + payloadLength + 1];
        entry[EncodedEntryLayout.SeedOffset] = unchecked((byte)seed);

        if (payloadLength == 0)
            return entry;

        Span<byte> encryptedBody = entry.AsSpan(EncodedEntryLayout.BodyOffset, payloadLength);
        WriteEncodedBody(value, masterKey, seed, encryptedBody);
        return entry;
    }

    /// <summary>
    /// Allocates one encoded StringPool entry in native memory and returns its address.
    /// </summary>
    public static uint AllocateEncodedEntry(
        INativeAllocator allocator,
        string value,
        ReadOnlySpan<byte> masterKey,
        sbyte seed = 0
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);

        byte[] entry = EncodeEntry(value, masterKey, seed);
        uint entryAddress = allocator.Allocate(entry.Length);
        if (!allocator.Write(entryAddress, entry))
        {
            allocator.Free(entryAddress);
            throw new InvalidOperationException("Failed to write the encoded StringPool entry.");
        }

        return entryAddress;
    }

    /// <summary>
    /// Replaces one static <c>ms_aString[index]</c> slot with a pre-allocated encoded entry pointer.
    /// </summary>
    public static void ReplaceStaticSlot(
        INativeAllocator allocator,
        StringPoolAddresses addresses,
        uint index,
        uint encodedEntryAddress
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);

        int slotCount = ReadStaticSlotCount(allocator, addresses);
        ValidateIndex(index, slotCount, nameof(index));

        uint slotAddress = checked(addresses.MsAString + (index * TypeSizes.Pointer));
        WritePointer(allocator, slotAddress, encodedEntryAddress, "static StringPool slot");
    }

    /// <summary>
    /// Encodes, allocates, and installs a new static <c>ms_aString[index]</c> entry.
    /// </summary>
    /// <returns>The native address of the encoded entry blob.</returns>
    public static uint ReplaceStaticSlot(
        INativeAllocator allocator,
        StringPoolAddresses addresses,
        uint index,
        string value,
        ReadOnlySpan<byte> masterKey,
        sbyte seed = 0
    )
    {
        uint encodedEntryAddress = AllocateEncodedEntry(allocator, value, masterKey, seed);
        ReplaceStaticSlot(allocator, addresses, index, encodedEntryAddress);
        return encodedEntryAddress;
    }

    /// <summary>
    /// Replaces the static entry and both live runtime caches for one StringPool index
    /// while holding the native StringPool lock.
    /// </summary>
    /// <remarks>
    /// This is the preferred mutation path when the goal is runtime-substitutable client
    /// semantics instead of a raw pointer poke. It validates that the live cache arrays still
    /// match <c>ms_nSize</c>, acquires <c>m_lock</c>, and updates all client-visible pointers as
    /// one logical operation with rollback on slot-write failure.
    /// </remarks>
    public static StringPoolRuntimeSubstitution SubstituteSlot(
        INativeRuntimeAllocator allocator,
        StringPoolAddresses addresses,
        uint stringPoolAddress,
        uint index,
        string value,
        ReadOnlySpan<byte> masterKey,
        sbyte seed = 0,
        int refCount = 1,
        int capacity = 0
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(value);

        uint encodedEntryAddress = AllocateEncodedEntry(allocator, value, masterKey, seed);
        uint narrowStringAddress = ZXString.Create(allocator, value, refCount, capacity);
        uint wideStringAddress = ZXStringWide.Create(allocator, value, refCount, capacity);

        using NativeStringPoolLockScope _ = NativeStringPoolLock.Acquire(allocator, stringPoolAddress);

        int slotCount = ReadStaticSlotCount(allocator, addresses);
        ValidateIndex(index, slotCount, nameof(index));

        uint narrowCachePayload = ReadCachePayloadAddress(
            allocator,
            stringPoolAddress,
            StringPoolLayout.NarrowCacheOffset,
            "narrow"
        );
        uint wideCachePayload = ReadCachePayloadAddress(
            allocator,
            stringPoolAddress,
            StringPoolLayout.WideCacheOffset,
            "wide"
        );

        ValidateRuntimeCacheShape(allocator, narrowCachePayload, slotCount, "narrow");
        ValidateRuntimeCacheShape(allocator, wideCachePayload, slotCount, "wide");

        uint staticSlotAddress = checked(addresses.MsAString + (index * TypeSizes.Pointer));
        uint narrowSlotAddress = checked(narrowCachePayload + (index * TypeSizes.Pointer));
        uint wideSlotAddress = checked(wideCachePayload + (index * TypeSizes.Pointer));

        WriteRuntimePointers(
            allocator,
            new SlotWrite(staticSlotAddress, encodedEntryAddress, "static StringPool slot"),
            new SlotWrite(narrowSlotAddress, narrowStringAddress, "narrow StringPool cache slot"),
            new SlotWrite(wideSlotAddress, wideStringAddress, "wide StringPool cache slot")
        );

        return new StringPoolRuntimeSubstitution(encodedEntryAddress, narrowStringAddress, wideStringAddress);
    }

    /// <summary>
    /// Injects a narrow <c>ZXString&lt;char&gt;</c> into the live <c>m_apZMString</c> cache.
    /// </summary>
    /// <remarks>
    /// This performs a raw cache-slot write without acquiring <c>StringPool::m_lock</c>.
    /// For live client substitution, prefer <see cref="SubstituteSlot"/>.
    /// </remarks>
    /// <returns>The native address of the injected <c>ZXString&lt;char&gt;</c> object.</returns>
    public static uint SetNarrowCacheSlot(
        INativeAllocator allocator,
        uint stringPoolAddress,
        uint index,
        string value,
        int refCount = 1,
        int capacity = 0
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(value);

        uint cachePayloadAddress = ReadCachePayloadAddress(
            allocator,
            stringPoolAddress,
            StringPoolLayout.NarrowCacheOffset,
            "narrow"
        );
        ValidateIndex(index, ReadArrayCount(allocator, cachePayloadAddress), nameof(index));

        uint zxStringAddress = ZXString.Create(allocator, value, refCount, capacity);
        WritePointer(
            allocator,
            checked(cachePayloadAddress + (index * TypeSizes.Pointer)),
            zxStringAddress,
            "narrow StringPool cache slot"
        );
        return zxStringAddress;
    }

    /// <summary>
    /// Injects a wide <c>ZXString&lt;unsigned short&gt;</c> into the live <c>m_apZWString</c> cache.
    /// </summary>
    /// <remarks>
    /// This performs a raw cache-slot write without acquiring <c>StringPool::m_lock</c>.
    /// For live client substitution, prefer <see cref="SubstituteSlot"/>.
    /// </remarks>
    /// <returns>The native address of the injected <c>ZXString&lt;unsigned short&gt;</c> object.</returns>
    public static uint SetWideCacheSlot(
        INativeAllocator allocator,
        uint stringPoolAddress,
        uint index,
        string value,
        int refCount = 1,
        int capacity = 0
    )
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(value);

        uint cachePayloadAddress = ReadCachePayloadAddress(
            allocator,
            stringPoolAddress,
            StringPoolLayout.WideCacheOffset,
            "wide"
        );
        ValidateIndex(index, ReadArrayCount(allocator, cachePayloadAddress), nameof(index));

        uint zxStringAddress = ZXStringWide.Create(allocator, value, refCount, capacity);
        WritePointer(
            allocator,
            checked(cachePayloadAddress + (index * TypeSizes.Pointer)),
            zxStringAddress,
            "wide StringPool cache slot"
        );
        return zxStringAddress;
    }

    private static void ValidateMasterKey(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(masterKey), "Master key must not be empty.");

        if (masterKey.Length > 256)
            throw new ArgumentOutOfRangeException(
                nameof(masterKey),
                masterKey.Length,
                "Master key length must not exceed 256 bytes."
            );
    }

    private static void WriteEncodedBody(string value, ReadOnlySpan<byte> masterKey, sbyte seed, Span<byte> destination)
    {
        if (destination.Length == 0)
            return;

        if (value.Length <= StackBufferBytes)
        {
            Span<byte> plain = stackalloc byte[StackBufferBytes];
            Span<byte> slice = plain[..value.Length];
            WriteLatin1Bytes(value, slice);
            RotatedKey key = new(masterKey, seed);
            StringPoolCrypto.Encode(slice, in key, destination);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(value.Length);
        try
        {
            Span<byte> plain = rented.AsSpan(0, value.Length);
            WriteLatin1Bytes(value, plain);
            RotatedKey key = new(masterKey, seed);
            StringPoolCrypto.Encode(plain, in key, destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private static void WriteLatin1Bytes(string value, Span<byte> destination)
    {
        if (destination.Length != value.Length)
            throw new ArgumentException("Destination length must match the string length.", nameof(destination));

        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch > byte.MaxValue)
            {
                throw new ArgumentException(
                    $"String contains non-Latin-1 character U+{(int)ch:X4} at index {i}.",
                    nameof(value)
                );
            }

            destination[i] = (byte)ch;
        }
    }

    private static int ReadStaticSlotCount(INativeAllocator allocator, StringPoolAddresses addresses)
    {
        Span<byte> countBytes = stackalloc byte[TypeSizes.Int32];
        if (!allocator.Read(addresses.MsNSize, countBytes))
            throw new InvalidOperationException($"Failed to read StringPool slot count at 0x{addresses.MsNSize:X8}.");

        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(countBytes));
    }

    private static uint ReadCachePayloadAddress(
        INativeAllocator allocator,
        uint stringPoolAddress,
        int cacheOffset,
        string cacheName
    )
    {
        Span<byte> pointerBytes = stackalloc byte[TypeSizes.Pointer];
        uint cacheFieldAddress = checked(stringPoolAddress + (uint)cacheOffset);
        if (!allocator.Read(cacheFieldAddress, pointerBytes))
        {
            throw new InvalidOperationException(
                $"Failed to read the {cacheName} cache pointer from StringPool at 0x{cacheFieldAddress:X8}."
            );
        }

        uint payloadAddress = BinaryPrimitives.ReadUInt32LittleEndian(pointerBytes);
        if (payloadAddress == 0)
            throw new InvalidOperationException($"The {cacheName} StringPool cache is not initialized.");

        return payloadAddress;
    }

    private static int ReadArrayCount(INativeAllocator allocator, uint payloadAddress)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan<uint>(payloadAddress, ZArrayLayout.HeaderBytes);

        Span<byte> countBytes = stackalloc byte[TypeSizes.Int32];
        uint headerAddress = payloadAddress - ZArrayLayout.HeaderBytes;
        if (!allocator.Read(headerAddress, countBytes))
            throw new InvalidOperationException($"Failed to read the ZArray count header at 0x{headerAddress:X8}.");

        return BinaryPrimitives.ReadInt32LittleEndian(countBytes);
    }

    private static uint ReadPointer(INativeAllocator allocator, uint address, string description)
    {
        Span<byte> pointerBytes = stackalloc byte[TypeSizes.Pointer];
        if (!allocator.Read(address, pointerBytes))
            throw new InvalidOperationException($"Failed to read the {description} at 0x{address:X8}.");

        return BinaryPrimitives.ReadUInt32LittleEndian(pointerBytes);
    }

    private static void ValidateRuntimeCacheShape(
        INativeAllocator allocator,
        uint payloadAddress,
        int expectedSlotCount,
        string cacheName
    )
    {
        int actualSlotCount = ReadArrayCount(allocator, payloadAddress);
        if (actualSlotCount != expectedSlotCount)
        {
            throw new InvalidOperationException(
                $"The {cacheName} StringPool cache has {actualSlotCount} slots, but ms_nSize is {expectedSlotCount}."
            );
        }
    }

    private static void WriteRuntimePointers(INativeAllocator allocator, params SlotWrite[] writes)
    {
        Span<uint> previousValues = stackalloc uint[writes.Length];
        int appliedWrites = 0;

        try
        {
            for (int i = 0; i < writes.Length; i++)
            {
                previousValues[i] = ReadPointer(allocator, writes[i].Address, writes[i].Description);
                WritePointer(allocator, writes[i].Address, writes[i].Value, writes[i].Description);
                appliedWrites++;
            }
        }
        catch
        {
            for (int i = appliedWrites - 1; i >= 0; i--)
                WritePointer(allocator, writes[i].Address, previousValues[i], $"rollback for {writes[i].Description}");

            throw;
        }
    }

    private static void ValidateIndex(uint index, int slotCount, string paramName)
    {
        if ((ulong)index >= (uint)slotCount)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                index,
                $"Index {index} is outside the available StringPool slot range 0..{slotCount - 1}."
            );
        }
    }

    private static void WritePointer(INativeAllocator allocator, uint address, uint value, string description)
    {
        Span<byte> pointerBytes = stackalloc byte[TypeSizes.Pointer];
        BinaryPrimitives.WriteUInt32LittleEndian(pointerBytes, value);
        if (!allocator.Write(address, pointerBytes))
            throw new InvalidOperationException($"Failed to write the {description} at 0x{address:X8}.");
    }

    private readonly record struct SlotWrite(uint Address, uint Value, string Description);
}
