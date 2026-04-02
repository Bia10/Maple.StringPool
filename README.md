# Maple.StringPool

![.NET](https://img.shields.io/badge/net10.0-5C2D91?logo=.NET&labelColor=gray)
![C#](https://img.shields.io/badge/C%23-14.0-239120?labelColor=gray)
[![Build Status](https://github.com/Bia10/Maple.StringPool/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/Bia10/Maple.StringPool/actions/workflows/dotnet.yml)
[![codecov](https://codecov.io/gh/Bia10/Maple.StringPool/branch/main/graph/badge.svg)](https://codecov.io/gh/Bia10/Maple.StringPool)
[![Nuget](https://img.shields.io/nuget/v/Maple.StringPool?color=purple)](https://www.nuget.org/packages/Maple.StringPool/)
[![License](https://img.shields.io/github/license/Bia10/Maple.StringPool)](https://github.com/Bia10/Maple.StringPool/blob/main/LICENSE)

Zero-allocation decoder for the MapleStory GMS v95 `StringPool` singleton. Cross-platform, trimmable and AOT/NativeAOT compatible.

The library also includes allocator-backed mutation helpers for live runtime substitution scenarios where an injected tool needs to replace an existing StringPool slot or seed the live runtime caches with a new `ZXString`.

⭐ Please star this project if you like it. ⭐

[Example](#example) | [Example Catalogue](#example-catalogue) | [CLI Tool (`sp`)](#cli-tool-sp) | [Public API Reference](docs/PublicApi.md)

## Runtime Mutation

The original client does not expose a growable StringPool. In v95, `ms_aString` is a fixed `const char*[6883]` table in `.data`, and `ms_nSize` is fixed as well. That means adding a new string in practice means reusing an existing slot unless you also patch the client code that assumes the original table shape.

For runtime tooling, the preferred operation is a synchronized whole-slot substitution:

```csharp
using Maple.Native;
using Maple.StringPool;

INativeRuntimeAllocator allocator = /* injected runtime allocator */;

StringPoolRuntimeSubstitution substitution = StringPoolMutator.SubstituteSlot(
    allocator,
    KnownLayouts.GmsV95,
    stringPoolAddress,
    index: 25,
    value: "Telemetry/Hero",
    masterKey: masterKeyBytes,
    seed: 0);
```

That path validates the live cache shape against `ms_nSize`, acquires `StringPool::m_lock`, and updates the static entry plus both runtime caches as one logical operation. It requires a runtime allocator that can participate in the target thread's lock ownership semantics; a raw out-of-process handle is not enough for this API by itself.

For raw remote memory patching from an attached Windows tool, Maple.Native now also exposes a Windows-only remote-process backend:

```csharp
using Maple.Memory;
using Maple.Native;
using Maple.Process;
using Maple.StringPool;

using WindowsProcessMemory processMemory = WindowsProcessMemory.Open(processId);
using RemoteProcessAllocator allocator = new(processMemory);

uint entryAddress = StringPoolMutator.ReplaceStaticSlot(
    allocator,
    KnownLayouts.GmsV95,
    index: 25,
    value: "Telemetry/Hero",
    masterKey: masterKeyBytes,
    seed: 0);
```

That remote allocator is suitable for allocator-backed native object creation and raw pointer replacement. The synchronized `SubstituteSlot` path still needs an injected `INativeRuntimeAllocator` implementation.

Lower-level operations still exist when you explicitly want raw pointer writes:

```csharp
using Maple.Native;
using Maple.StringPool;

INativeAllocator allocator = /* future remote allocator */;

// Replace the encoded entry used by the static table.
uint entryAddress = StringPoolMutator.ReplaceStaticSlot(
    allocator,
    KnownLayouts.GmsV95,
    index: 25,
    value: "Telemetry/Hero",
    masterKey: masterKeyBytes,
    seed: 0);

// Or inject a ready-made runtime cache entry for immediate GetString() use.
uint liveNarrow = StringPoolMutator.SetNarrowCacheSlot(
    allocator,
    stringPoolAddress,
    index: 25,
    value: "Telemetry/Hero");

uint liveWide = StringPoolMutator.SetWideCacheSlot(
    allocator,
    stringPoolAddress,
    index: 25,
    value: "Telemetry/Hero");
```

The static-slot path updates `ms_aString[index]`. The raw live-cache path writes directly to `m_apZMString[index]` and `m_apZWString[index]` without taking `m_lock`, so it is mainly for controlled tooling and tests. For live-client replacement, `SubstituteSlot` is the safer API.

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

The following examples are available in [ReadMeTest.cs](src/Maple.StringPool.DocTest/ReadMeTest.cs).

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

See [docs/PublicApi.md](docs/PublicApi.md) for the full generated public API surface.

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
