using System.Buffers.Binary;
using Maple.Native;
using Maple.StringPool.Crypto;
using Maple.StringPool.NativeTypes;

namespace Maple.StringPool.Test.Mutator;

public sealed class StringPoolMutatorTests
{
    private static readonly byte[] TestMasterKey = [0x6C, 0xEB, 0xBC, 0x5E, 0x4B, 0xCC, 0x47, 0x5D];

    [Test]
    public async Task EncodeEntry_RoundTripsThroughDecode()
    {
        byte[] entry = StringPoolMutator.EncodeEntry("Hero", TestMasterKey, seed: 8);

        await Assert.That(entry[EncodedEntryLayout.SeedOffset]).IsEqualTo((byte)8);

        RotatedKey key = new(TestMasterKey, seed: 8);
        Span<byte> plain = stackalloc byte[entry.Length - EncodedEntryLayout.BodyOffset - 1];
        StringPoolCrypto.Decode(entry.AsSpan(EncodedEntryLayout.BodyOffset, plain.Length), in key, plain);

        await Assert.That(System.Text.Encoding.Latin1.GetString(plain)).IsEqualTo("Hero");
        await Assert.That(entry[^1]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task ReplaceStaticSlot_WritesEncodedEntryPointer()
    {
        using var allocator = new InProcessAllocator();

        uint slotCountAddress = allocator.Allocate(TypeSizes.UInt32);
        uint pointerTableAddress = allocator.Allocate(3 * TypeSizes.Pointer);
        Span<byte> countBytes = stackalloc byte[TypeSizes.UInt32];
        BinaryPrimitives.WriteUInt32LittleEndian(countBytes, 3);
        allocator.Write(slotCountAddress, countBytes);

        var addresses = new StringPoolAddresses
        {
            ImageBase = 0,
            MsAString = pointerTableAddress,
            MsAKey = 0,
            MsNKeySize = 0,
            MsNSize = slotCountAddress,
        };

        uint entryAddress = StringPoolMutator.ReplaceStaticSlot(allocator, addresses, 1, "Hero", TestMasterKey);

        await Assert.That(allocator.ReadUInt32(pointerTableAddress + TypeSizes.Pointer)).IsEqualTo(entryAddress);
    }

    [Test]
    public async Task ReplaceStaticSlot_WhenIndexIsOutOfRange_Throws()
    {
        using var allocator = new InProcessAllocator();

        uint slotCountAddress = allocator.Allocate(TypeSizes.UInt32);
        uint pointerTableAddress = allocator.Allocate(TypeSizes.Pointer);
        Span<byte> countBytes = stackalloc byte[TypeSizes.UInt32];
        BinaryPrimitives.WriteUInt32LittleEndian(countBytes, 1);
        allocator.Write(slotCountAddress, countBytes);

        var addresses = new StringPoolAddresses
        {
            ImageBase = 0,
            MsAString = pointerTableAddress,
            MsAKey = 0,
            MsNKeySize = 0,
            MsNSize = slotCountAddress,
        };

        await Assert
            .That(() => StringPoolMutator.ReplaceStaticSlot(allocator, addresses, 1, "Hero", TestMasterKey))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SetNarrowCacheSlot_WritesInjectedZXString()
    {
        using var allocator = new InProcessAllocator();

        NativeStringPoolAllocation pool = NativeStringPool.AllocateEmpty(allocator, 3);
        uint zxStringAddress = StringPoolMutator.SetNarrowCacheSlot(allocator, pool.ObjectAddress, 1, "Hero");

        await Assert.That(allocator.ReadUInt32(pool.NarrowCachePayload + TypeSizes.Pointer)).IsEqualTo(zxStringAddress);

        uint payloadAddress = allocator.ReadUInt32(zxStringAddress);
        byte[] data = allocator.ReadBytes(
            payloadAddress - ZXStringDataLayout.HeaderBytes,
            ZXStringDataLayout.HeaderBytes + 5
        );
        ZXString value = ZXString.ReadFrom(data, ZXStringDataLayout.PayloadOffset);

        await Assert.That(value.Value).IsEqualTo("Hero");
    }

    [Test]
    public async Task SetWideCacheSlot_WritesInjectedZXStringWide()
    {
        using var allocator = new InProcessAllocator();

        NativeStringPoolAllocation pool = NativeStringPool.AllocateEmpty(allocator, 3);
        uint zxStringAddress = StringPoolMutator.SetWideCacheSlot(allocator, pool.ObjectAddress, 2, "Hero");

        await Assert
            .That(allocator.ReadUInt32(pool.WideCachePayload + (2 * TypeSizes.Pointer)))
            .IsEqualTo(zxStringAddress);

        uint payloadAddress = allocator.ReadUInt32(zxStringAddress);
        byte[] data = allocator.ReadBytes(
            payloadAddress - ZXStringDataLayout.HeaderBytes,
            ZXStringDataLayout.HeaderBytes + ("Hero".Length * 2)
        );
        ZXStringWide value = ZXStringWide.ReadFrom(data, ZXStringDataLayout.PayloadOffset);

        await Assert.That(value.Value).IsEqualTo("Hero");
    }

    [Test]
    public async Task SubstituteSlot_UpdatesStaticAndBothCaches_AndReleasesLock()
    {
        using var allocator = new InProcessAllocator();

        StringPoolAddresses addresses = CreateAddresses(allocator, slotCount: 3);
        NativeStringPoolAllocation pool = NativeStringPool.AllocateEmpty(allocator, 3);

        StringPoolRuntimeSubstitution substitution = StringPoolMutator.SubstituteSlot(
            allocator,
            addresses,
            pool.ObjectAddress,
            index: 1,
            value: "Hero",
            masterKey: TestMasterKey,
            seed: 8
        );

        await Assert
            .That(allocator.ReadUInt32(addresses.MsAString + TypeSizes.Pointer))
            .IsEqualTo(substitution.EncodedEntryAddress);
        await Assert
            .That(allocator.ReadUInt32(pool.NarrowCachePayload + TypeSizes.Pointer))
            .IsEqualTo(substitution.NarrowStringAddress);
        await Assert
            .That(allocator.ReadUInt32(pool.WideCachePayload + TypeSizes.Pointer))
            .IsEqualTo(substitution.WideStringAddress);

        ZFatalSection lockState = NativeStringPoolLock.Read(allocator, pool.ObjectAddress);
        await Assert.That(lockState.TibPointer).IsEqualTo(0u);
        await Assert.That(lockState.RefCount).IsEqualTo(0);
    }

    [Test]
    public async Task SubstituteSlot_WhenSlotWriteFails_RollsBackAndReleasesLock()
    {
        using var innerAllocator = new InProcessAllocator();

        StringPoolAddresses addresses = CreateAddresses(innerAllocator, slotCount: 3);
        NativeStringPoolAllocation pool = NativeStringPool.AllocateEmpty(innerAllocator, 3);
        uint staticSlotAddress = addresses.MsAString + TypeSizes.Pointer;
        uint narrowSlotAddress = pool.NarrowCachePayload + TypeSizes.Pointer;
        uint wideSlotAddress = pool.WideCachePayload + TypeSizes.Pointer;

        WritePointer(innerAllocator, staticSlotAddress, 0x1111_1111u);
        WritePointer(innerAllocator, narrowSlotAddress, 0x2222_2222u);
        WritePointer(innerAllocator, wideSlotAddress, 0x3333_3333u);

        using var allocator = new FailingRuntimeAllocator(innerAllocator, wideSlotAddress);

        await Assert
            .That(() =>
                StringPoolMutator.SubstituteSlot(
                    allocator,
                    addresses,
                    pool.ObjectAddress,
                    index: 1,
                    value: "Hero",
                    masterKey: TestMasterKey
                )
            )
            .Throws<InvalidOperationException>();

        await Assert.That(innerAllocator.ReadUInt32(staticSlotAddress)).IsEqualTo(0x1111_1111u);
        await Assert.That(innerAllocator.ReadUInt32(narrowSlotAddress)).IsEqualTo(0x2222_2222u);
        await Assert.That(innerAllocator.ReadUInt32(wideSlotAddress)).IsEqualTo(0x3333_3333u);

        ZFatalSection lockState = NativeStringPoolLock.Read(innerAllocator, pool.ObjectAddress);
        await Assert.That(lockState.TibPointer).IsEqualTo(0u);
        await Assert.That(lockState.RefCount).IsEqualTo(0);
    }

    [Test]
    public async Task SubstituteSlot_WhenCacheShapeDiffersFromMsNSize_Throws()
    {
        using var allocator = new InProcessAllocator();

        StringPoolAddresses addresses = CreateAddresses(allocator, slotCount: 3);
        NativeStringPoolAllocation pool = NativeStringPool.AllocateEmpty(allocator, 2);

        await Assert
            .That(() =>
                StringPoolMutator.SubstituteSlot(
                    allocator,
                    addresses,
                    pool.ObjectAddress,
                    index: 1,
                    value: "Hero",
                    masterKey: TestMasterKey
                )
            )
            .Throws<InvalidOperationException>();
    }

    private static StringPoolAddresses CreateAddresses(InProcessAllocator allocator, uint slotCount)
    {
        uint slotCountAddress = allocator.Allocate(TypeSizes.UInt32);
        uint pointerTableAddress = allocator.Allocate((int)(slotCount * TypeSizes.Pointer));
        Span<byte> countBytes = stackalloc byte[TypeSizes.UInt32];
        BinaryPrimitives.WriteUInt32LittleEndian(countBytes, slotCount);
        allocator.Write(slotCountAddress, countBytes);

        return new StringPoolAddresses
        {
            ImageBase = 0,
            MsAString = pointerTableAddress,
            MsAKey = 0,
            MsNKeySize = 0,
            MsNSize = slotCountAddress,
        };
    }

    private static void WritePointer(INativeAllocator allocator, uint address, uint value)
    {
        Span<byte> pointerBytes = stackalloc byte[TypeSizes.Pointer];
        BinaryPrimitives.WriteUInt32LittleEndian(pointerBytes, value);
        allocator.Write(address, pointerBytes);
    }

    private sealed class FailingRuntimeAllocator : INativeRuntimeAllocator, IDisposable
    {
        private readonly InProcessAllocator _inner;
        private readonly uint _failAddress;
        private bool _shouldFail = true;

        public FailingRuntimeAllocator(InProcessAllocator inner, uint failAddress)
        {
            _inner = inner;
            _failAddress = failAddress;
        }

        public uint CurrentThreadTeb => _inner.CurrentThreadTeb;

        public uint Allocate(int size) => _inner.Allocate(size);

        public bool Read(uint address, Span<byte> destination) => _inner.Read(address, destination);

        public bool Write(uint address, ReadOnlySpan<byte> data)
        {
            if (_shouldFail && address == _failAddress)
            {
                _shouldFail = false;
                return false;
            }

            return _inner.Write(address, data);
        }

        public void Free(uint address) => _inner.Free(address);

        public bool CompareExchangeUInt32(uint address, uint expected, uint desired, out uint observed) =>
            _inner.CompareExchangeUInt32(address, expected, desired, out observed);

        public void YieldThread() => _inner.YieldThread();

        public void Dispose() { }
    }
}
