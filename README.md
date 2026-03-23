# Maple.StringPool

![.NET](https://img.shields.io/badge/net10.0-5C2D91?logo=.NET&labelColor=gray)
![C#](https://img.shields.io/badge/C%23-14.0-239120?labelColor=gray)
[![Build Status](https://github.com/Bia10/Maple.StringPool/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/Bia10/Maple.StringPool/actions/workflows/dotnet.yml)
[![codecov](https://codecov.io/gh/Bia10/Maple.StringPool/branch/main/graph/badge.svg)](https://codecov.io/gh/Bia10/Maple.StringPool)
[![Nuget](https://img.shields.io/nuget/v/Maple.StringPool?color=purple)](https://www.nuget.org/packages/Maple.StringPool/)
[![License](https://img.shields.io/github/license/Bia10/Maple.StringPool)](https://github.com/Bia10/Maple.StringPool/blob/main/LICENSE)

Zero-allocation decoder for the MapleStory GMS v95 `StringPool` singleton. Cross-platform, trimmable and AOT/NativeAOT compatible.

⭐ Please star this project if you like it. ⭐

[Example](#example) | [Example Catalogue](#example-catalogue) | [CLI Tool (`sp`)](#cli-tool-sp) | [Public API Reference](#public-api-reference)

## Example

```csharp
// Demonstration — requires a real MapleStory.exe; skip if not present.
const string exePath = "MapleStory.exe";
if (!File.Exists(exePath))
    return;

using var pool = StringPoolDecoder.Open(exePath);

// Single slot by index (decimal or hex)
string hero = pool.GetString(25); // "Hero"
Console.WriteLine(hero);

// Enumerate all entries
foreach (StringPoolEntry e in pool.GetAll())
    Console.WriteLine(e); // SP[0x19] (25): Hero

// Case-insensitive substring search
foreach (StringPoolEntry e in pool.Find("warrior"))
    Console.WriteLine(e);

// Slice [start, end)
foreach (StringPoolEntry e in pool.GetRange(0, 100))
    Console.WriteLine(e);
```

For more examples see [Example Catalogue](#example-catalogue).

## Benchmarks

Benchmarks.

### Detailed Benchmarks

#### Comparison Benchmarks

##### TestBench Benchmark Results


## Example Catalogue

The following examples are available in [ReadMeTest.cs](src/Maple.StringPool.XyzTest/ReadMeTest.cs).

### Example - Empty

```csharp
// Demonstration — requires a real MapleStory.exe; skip if not present.
const string exePath = "MapleStory.exe";
if (!File.Exists(exePath))
    return;

using var pool = StringPoolDecoder.Open(exePath);

// Single slot by index (decimal or hex)
string hero = pool.GetString(25); // "Hero"
Console.WriteLine(hero);

// Enumerate all entries
foreach (StringPoolEntry e in pool.GetAll())
    Console.WriteLine(e); // SP[0x19] (25): Hero

// Case-insensitive substring search
foreach (StringPoolEntry e in pool.Find("warrior"))
    Console.WriteLine(e);

// Slice [start, end)
foreach (StringPoolEntry e in pool.GetRange(0, 100))
    Console.WriteLine(e);
```

## CLI Tool (`sp`)

The `sp` command-line tool wraps `StringPoolDecoder` for interactive use by humans and agents.

### Installation

Download the latest `sp.exe` from [GitHub Releases](https://github.com/Bia10/Maple.StringPool/releases) or build from source:

```shell
dotnet publish src/Maple.StringPool.Cli/Maple.StringPool.Cli.csproj -c Release -r win-x64
```

### Usage

```
sp <MapleStory.exe> <command> [options]

Commands:
  get   <index>              Decode SP[index] — accepts decimal or 0x hex
  range <start> <end>        Decode slots [start, end)
  find  <term>               Search all slots for <term> (case-insensitive)
  dump                       Dump every slot
  info                       Print metadata: count, key-size, master-key hex

Options (range, find, dump):
  --format text|json|csv     Output format (default: text)
  --filter <term>            Only emit entries containing <term> (range, dump)
  --out <file>               Write output to <file> instead of stdout
```

### Examples

```shell
sp MapleStory.exe get 8
sp MapleStory.exe get 0x19
sp MapleStory.exe range 0 100
sp MapleStory.exe range 0 100 --format json --out slice.json
sp MapleStory.exe find Warrior
sp MapleStory.exe find Warrior --format csv
sp MapleStory.exe dump --format csv --out strings.csv
sp MapleStory.exe dump --filter UI/Login
sp MapleStory.exe info
```

## Public API Reference

```csharp
[assembly: System.Reflection.AssemblyMetadata("IsAotCompatible", "True")]
[assembly: System.Reflection.AssemblyMetadata("IsTrimmable", "True")]
[assembly: System.Reflection.AssemblyMetadata("RepositoryUrl", "https://github.com/Bia10/Maple.StringPool/")]
[assembly: System.Resources.NeutralResourcesLanguage("en")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Maple.StringPool.Benchmarks")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Maple.StringPool.ComparisonBenchmarks")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Maple.StringPool.Test")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Maple.StringPool.XyzTest")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v10.0", FrameworkDisplayName=".NET 10.0")]
namespace Maple.StringPool
{
    public static class KnownLayouts
    {
        public static readonly Maple.StringPool.StringPoolAddresses GmsV95;
    }
    public readonly struct StringPoolAddresses : System.IEquatable<Maple.StringPool.StringPoolAddresses>
    {
        public required uint ImageBase { get; init; }
        public required uint MsAKey { get; init; }
        public required uint MsAString { get; init; }
        public required uint MsNKeySize { get; init; }
        public required uint MsNSize { get; init; }
    }
    public sealed class StringPoolDecoder : System.IDisposable
    {
        public StringPoolDecoder(Maple.StringPool.Source.IPeImageReader reader, Maple.StringPool.StringPoolAddresses? addresses = default) { }
        public int Count { get; }
        public int KeySize { get; }
        public System.ReadOnlySpan<byte> MasterKey { get; }
        public void Dispose() { }
        public System.Collections.Generic.IEnumerable<Maple.StringPool.StringPoolEntry> Find(string term, System.StringComparison comparison = 5) { }
        public System.Collections.Generic.IEnumerable<Maple.StringPool.StringPoolEntry> GetAll() { }
        public string GetBSTR(uint index) { }
        public System.Collections.Generic.IEnumerable<Maple.StringPool.StringPoolEntry> GetRange(uint start, uint end) { }
        public string GetString(uint index) { }
        public string GetStringW(uint index) { }
        public static Maple.StringPool.StringPoolDecoder FromBytes(byte[] peImage, Maple.StringPool.StringPoolAddresses? addresses = default) { }
        public static Maple.StringPool.StringPoolDecoder Open(string exePath, Maple.StringPool.StringPoolAddresses? addresses = default) { }
    }
    public readonly struct StringPoolEntry : System.IEquatable<Maple.StringPool.StringPoolEntry>
    {
        public StringPoolEntry(uint Index, string Value) { }
        public uint Index { get; init; }
        public string Value { get; init; }
        public override string ToString() { }
    }
}
namespace Maple.StringPool.NativeTypes
{
    public readonly ref struct EncodedEntryLayout
    {
        public const int BodyOffset = 1;
        public const int SeedBytes = 1;
        public const int SeedOffset = 0;
    }
    public readonly ref struct StringPoolKeyLayout
    {
        public const int KeyArrayOffset = 0;
        public const int TotalBytes = 4;
    }
    public readonly ref struct StringPoolLayout
    {
        public const int LockOffset = 8;
        public const int NarrowCacheOffset = 0;
        public const int TotalBytes = 16;
        public const int WideCacheOffset = 4;
    }
    public static class TypeSizes
    {
        public const int Int32 = 4;
        public const int Pointer = 4;
    }
    public static class ZArray
    {
        public static byte[] ReadByteElements(System.ReadOnlySpan<byte> image, int payloadFileOffset, int count) { }
        public static int ReadCount(System.ReadOnlySpan<byte> image, int payloadFileOffset) { }
        public static uint[] ReadPointerElements(System.ReadOnlySpan<byte> image, int payloadFileOffset, int count) { }
    }
    public readonly ref struct ZArrayLayout
    {
        public const int CountOffset = 0;
        public const int HeaderBytes = 4;
        public const int PayloadOffset = 4;
        public ZArrayLayout(int elementCount) { }
        public int ElementCount { get; }
        public int TotalBytes(int elementSize) { }
    }
    public readonly struct ZFatalSection
    {
        public ZFatalSection(uint tibPointer, int refCount) { }
        public int RefCount { get; }
        public uint TibPointer { get; }
        public static Maple.StringPool.NativeTypes.ZFatalSection Unlocked { get; }
    }
    public readonly ref struct ZFatalSectionLayout
    {
        public const int RefCountOffset = 4;
        public const int TibPointerOffset = 0;
        public const int TotalBytes = 8;
    }
    public readonly struct ZXString
    {
        public ZXString(string value, int refCount = 1, int capacity = 0, int byteLength = 0) { }
        public int ByteLength { get; }
        public int Capacity { get; }
        public int RefCount { get; }
        public string Value { get; }
        public override string ToString() { }
        public static Maple.StringPool.NativeTypes.ZXString ReadFrom(System.ReadOnlySpan<byte> image, int payloadFileOffset) { }
        public static string op_Implicit(Maple.StringPool.NativeTypes.ZXString s) { }
    }
    public readonly ref struct ZXStringDataLayout
    {
        public const int ByteLengthOffset = 8;
        public const int CapacityOffset = 4;
        public const int HeaderBytes = 12;
        public const int NullTerminatorBytes = 1;
        public const int PayloadOffset = 12;
        public const int RefCountOffset = 0;
        public ZXStringDataLayout(int payloadBytes) { }
        public int TotalBytes { get; }
    }
}
namespace Maple.StringPool.Source
{
    public interface IPeImageReader : System.IDisposable
    {
        System.ReadOnlyMemory<byte> Image { get; }
    }
    public sealed class MemoryPeImageReader : Maple.StringPool.Source.IPeImageReader, System.IDisposable
    {
        public System.ReadOnlyMemory<byte> Image { get; }
        public void Dispose() { }
        public static Maple.StringPool.Source.MemoryPeImageReader FromBytes(byte[] image) { }
        public static Maple.StringPool.Source.MemoryPeImageReader FromFile(string path) { }
    }
}
```

## Design Notes

- **Zero heap allocations per decode** — `RotatedKey` lives on the stack via `[InlineArray(256)]`; the XOR body is decrypted into a `stackalloc` buffer. The only allocation is the final `string`.
- **Thread safety** — `GetString` uses `Interlocked.CompareExchange` to publish decoded strings; concurrent callers may decode redundantly but only one result is stored.
- **Cipher** — circular left-rotation of the 16-byte master key by the per-entry seed byte, then XOR with zero-collision rule.

## Verified Addresses (GMS v95 PDB)

| Field | Address | Value |
|---|---|---|
| `ms_aString` | `0xC5A878` | `const char*[6883]` pointer table |
| `ms_aKey` | `0xB98830` | 16-byte master XOR key |
| `ms_nKeySize` | `0xB98840` | `16` |
| `ms_nSize` | `0xB98844` | `6883` (`0x1AE3`) |
