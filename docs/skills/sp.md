---
name: sp
description: Decode or search the GMS v95 MapleStory StringPool from a PE binary. Use this skill to look up string IDs, dump all strings, or search by substring.
---

# sp — Maple.StringPool CLI

Wraps `StringPoolDecoder` as a command-line tool for agents and researchers to query the GMS v95 `StringPool` directly from any `MapleStory.exe` binary.

## Prerequisites

- `sp.exe` on PATH **or** the full path to the published binary
- A GMS v95 `MapleStory.exe` binary (default layout `KnownLayouts.GmsV95`)

## Commands

### `get <index>`

Decode a single string slot. Accepts decimal or `0x` hex.

```shell
sp MapleStory.exe get 25        # → SP[0x19] (25): Hero
sp MapleStory.exe get 0x19      # same
sp MapleStory.exe get 8         # → SP[0x8] (8): Tahoma
```

### `range <start> <end>`

Decode slots `[start, end)`.

```shell
sp MapleStory.exe range 0 100
sp MapleStory.exe range 0 100 --format json --out slice.json
sp MapleStory.exe range 0 100 --format csv
sp MapleStory.exe range 0 50 --filter UI/Login
```

### `find <term>`

Search all slots for a case-insensitive substring.

```shell
sp MapleStory.exe find Warrior
sp MapleStory.exe find Warrior --format csv
sp MapleStory.exe find "UI/Login"
```

### `dump`

Dump all 6883 string slots.

```shell
sp MapleStory.exe dump
sp MapleStory.exe dump --format json --out all_strings.json
sp MapleStory.exe dump --format csv --out all_strings.csv
sp MapleStory.exe dump --filter UI/Login
```

### `info`

Print pool metadata: slot count, key size, master key hex.

```shell
sp MapleStory.exe info
# count     : 6883 (0x1AE3)
# key-size  : 16
# master-key: 6CEB...
```

## Options

| Option | Commands | Description |
|---|---|---|
| `--format text\|json\|csv` | range, find, dump | Output format (default: `text`) |
| `--filter <term>` | range, dump | Only emit entries containing `<term>` |
| `--out <file>` | range, find, dump | Write output to file instead of stdout |

## Output Formats

### `text` (default)

```
SP[0x8] (8): Tahoma
SP[0xC] (12): Beginner
SP[0xD] (13): Warrior
SP[0x19] (25): Hero
```

### `json`

```json
{
  "8": "Tahoma",
  "12": "Beginner"
}
```

### `csv`

```
Index,Key,Value
8,SP[0x8],"Tahoma"
12,SP[0xC],"Beginner"
```

## Notes for Agents

- Control characters (`\r`, `\n`) in string values are escaped as `\r`/`\n` literals in text and CSV output.
- Exit code `0` = success; non-zero = error (details on stderr).
- `find` with no matches exits with code `0` but prints `[info] no matches for '...'` to stderr.
- `sp` requires `.NET 10` runtime (not self-contained). On CI, ensure the runtime is installed.
- For programmatic use, prefer the `Maple.StringPool` NuGet package (`StringPoolDecoder` class) over shelling out to `sp`.
