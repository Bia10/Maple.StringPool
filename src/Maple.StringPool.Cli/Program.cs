using System.Text;
using System.Text.Json;
using Maple.StringPool;

namespace Maple.StringPool.Cli;

/// <summary>
/// Entry point for the <c>sp</c> command-line tool.
/// Wraps <see cref="StringPoolDecoder"/> so RE agents and researchers can
/// query the GMS v95 StringPool directly from any MapleStory.exe binary.
/// </summary>
internal static class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private const string Usage = """
        sp — GMS v95 StringPool decoder
        Usage:  sp <MapleStory.exe> <command> [options]

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

        Examples:
          sp MapleStory.exe get 8
          sp MapleStory.exe get 0x19
          sp MapleStory.exe range 0 100
          sp MapleStory.exe range 0 100 --format json --out slice.json
          sp MapleStory.exe find Warrior
          sp MapleStory.exe find Warrior --format csv
          sp MapleStory.exe dump --format csv --out strings.csv
          sp MapleStory.exe dump --filter UI/Login
          sp MapleStory.exe info
        """;

    internal static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        string exePath = args[0];
        string command = args[1].ToLowerInvariant();

        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"[error] file not found: {exePath}");
            return 1;
        }

        StringPoolDecoder pool;
        try
        {
            pool = StringPoolDecoder.Open(exePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] failed to open StringPool: {ex.Message}");
            return 1;
        }

        using (pool)
        {
            return command switch
            {
                "get" => RunGet(pool, args[2..]),
                "range" => RunRange(pool, args[2..]),
                "find" => RunFind(pool, args[2..]),
                "dump" => RunDump(pool, args[2..]),
                "info" => RunInfo(pool),
                _ => BadCommand(command),
            };
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private static int RunGet(StringPoolDecoder pool, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("[error] get: missing <index>");
            return 1;
        }

        if (!TryParseIndex(args[0], out uint index))
        {
            Console.Error.WriteLine($"[error] get: invalid index '{args[0]}'");
            return 1;
        }

        if (index >= (uint)pool.Count)
        {
            Console.Error.WriteLine($"[error] get: index {index} out of range (pool has {pool.Count} slots)");
            return 1;
        }

        Console.WriteLine(new StringPoolEntry(index, pool.GetString(index)));
        return 0;
    }

    private static int RunRange(StringPoolDecoder pool, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("[error] range: expected <start> <end> [options]");
            return 1;
        }

        if (!TryParseIndex(args[0], out uint start) || !TryParseIndex(args[1], out uint end))
        {
            Console.Error.WriteLine($"[error] range: invalid arguments '{args[0]}' '{args[1]}'");
            return 1;
        }

        if (start > end)
        {
            Console.Error.WriteLine($"[error] range: start ({start}) must be ≤ end ({end})");
            return 1;
        }

        ParseOutputOptions(args[2..], out string format, out string? outPath, out string? filter);

        IEnumerable<StringPoolEntry> entries = pool.GetRange(start, end);
        if (filter is not null)
            entries = entries.Where(e => e.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));

        WriteResults(entries, format, outPath);
        return 0;
    }

    private static int RunFind(StringPoolDecoder pool, string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("[error] find: missing <term>");
            return 1;
        }

        // Collect term words up to the first '--' option flag.
        int optStart = Array.FindIndex(args, a => a.StartsWith("--", StringComparison.Ordinal));
        string term = string.Join(' ', optStart < 0 ? args : args[..optStart]);
        ParseOutputOptions(optStart < 0 ? [] : args[optStart..], out string format, out string? outPath, out _);

        IEnumerable<StringPoolEntry> hits = pool.Find(term);
        int written = WriteResults(hits, format, outPath);
        if (written == 0)
            Console.Error.WriteLine($"[info] no matches for '{term}'");
        return 0;
    }

    private static int RunDump(StringPoolDecoder pool, string[] args)
    {
        ParseOutputOptions(args, out string format, out string? outPath, out string? filter);

        IEnumerable<StringPoolEntry> entries = pool.GetAll();
        if (filter is not null)
            entries = entries.Where(e => e.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));

        WriteResults(entries, format, outPath);
        return 0;
    }

    private static int RunInfo(StringPoolDecoder pool)
    {
        Console.WriteLine($"count     : {pool.Count} (0x{pool.Count:X})");
        Console.WriteLine($"key-size  : {pool.KeySize}");
        Console.WriteLine($"master-key: {Convert.ToHexString(pool.MasterKey)}");
        return 0;
    }

    // ── Output ────────────────────────────────────────────────────────────────

    private static int WriteResults(IEnumerable<StringPoolEntry> entries, string format, string? outPath)
    {
        // Reuse Console.Out directly (already buffered) — only allocate a StreamWriter for file output.
        bool ownsWriter = outPath is not null;
        TextWriter output = ownsWriter ? new StreamWriter(outPath!, false, Utf8NoBom) : Console.Out;
        int count = 0;

        try
        {
            switch (format)
            {
                case "json":
                {
                    // Flush buffered text before switching to byte-level writer.
                    // Write incrementally via Utf8JsonWriter — avoids materialising a Dictionary<int, string>.
                    output.Flush();
                    Stream byteStream = ownsWriter ? ((StreamWriter)output).BaseStream : Console.OpenStandardOutput();
                    using var jw = new Utf8JsonWriter(byteStream, new JsonWriterOptions { Indented = true });
                    jw.WriteStartObject();
                    foreach (StringPoolEntry entry in entries)
                    {
                        jw.WritePropertyName(((int)entry.Index).ToString());
                        jw.WriteStringValue(entry.Value);
                        count++;
                    }
                    jw.WriteEndObject();
                    break;
                }

                case "csv":
                {
                    output.WriteLine("Index,Key,Value");
                    foreach (StringPoolEntry entry in entries)
                    {
                        output.Write($"{entry.Index},SP[0x{entry.Index:X}],\"");
                        WriteCsvEscaped(output, entry.Value);
                        output.WriteLine('"');
                        count++;
                    }

                    break;
                }

                case "text":
                default:
                {
                    if (format is not ("text" or "json" or "csv"))
                        Console.Error.WriteLine($"[warn] unknown format '{format}', using 'text'");

                    foreach (StringPoolEntry entry in entries)
                    {
                        output.Write($"SP[0x{entry.Index:X}] ({entry.Index}): ");
                        WriteTextEscaped(output, entry.Value);
                        output.WriteLine();
                        count++;
                    }

                    break;
                }
            }
        }
        finally
        {
            if (ownsWriter)
                output.Dispose();
        }

        return count;
    }

    // ── Option parsing ────────────────────────────────────────────────────────

    private static void ParseOutputOptions(string[] args, out string format, out string? outPath, out string? filter)
    {
        format = "text";
        outPath = null;
        filter = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--format" when i + 1 < args.Length:
                    format = args[++i].ToLowerInvariant();
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--filter" when i + 1 < args.Length:
                    filter = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"[warn] unknown option '{args[i]}'");
                    break;
            }
        }
    }

    private static int BadCommand(string cmd)
    {
        Console.Error.WriteLine($"[error] unknown command '{cmd}'");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Write CR and LF as \r and \n literals -- single pass, no intermediate string allocation.
    private static void WriteTextEscaped(TextWriter w, string value)
    {
        foreach (char c in value.AsSpan())
        {
            switch (c)
            {
                case '\r':
                    w.Write(@"\r");
                    break;
                case '\n':
                    w.Write(@"\n");
                    break;
                default:
                    w.Write(c);
                    break;
            }
        }
    }

    // Write a CSV-quoted value with ", CR, and LF as \r and \n literals -- single pass, no intermediate string allocation.
    private static void WriteCsvEscaped(TextWriter w, string value)
    {
        foreach (char c in value.AsSpan())
        {
            switch (c)
            {
                case '"':
                    w.Write("\"\"");
                    break;
                case '\r':
                    w.Write(@"\r");
                    break;
                case '\n':
                    w.Write(@"\n");
                    break;
                default:
                    w.Write(c);
                    break;
            }
        }
    }

    private static bool TryParseIndex(string s, out uint result)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out result);
        return uint.TryParse(s, out result);
    }
}
