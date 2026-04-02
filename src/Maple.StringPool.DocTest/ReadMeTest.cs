#pragma warning disable CA2007 // ConfigureAwait
#pragma warning disable CA1822 // Mark as static

using System.Diagnostics;
using System.Runtime.CompilerServices;
using PublicApiGenerator;

namespace Maple.StringPool.DocTest;

[NotInParallel]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public partial class ReadMeTest
{
    const string UpdateReadMeEnvironmentVariable = "MAPLE_UPDATE_README";

    static readonly string s_testSourceFilePath = SourceFile();
    static readonly string s_rootDirectory =
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(s_testSourceFilePath)!, "..", ".."))
        + Path.DirectorySeparatorChar;
    static readonly string s_readmeFilePath = s_rootDirectory + "README.md";
    static readonly string s_publicApiFilePath =
        s_rootDirectory + "docs" + Path.DirectorySeparatorChar + "PublicApi.md";

    [Test]
    public void ReadMeTest_()
    {
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
    }

#if NET10_0
    [Test]
#endif
    public void ReadMeTest_UpdateExampleCodeInMarkdown()
    {
        if (!ShouldUpdateReadMe())
        {
            return;
        }

        if (!File.Exists(s_readmeFilePath))
        {
            return;
        }

        var readmeLines = File.ReadAllLines(s_readmeFilePath);
        var testSourceLines = File.ReadAllLines(s_testSourceFilePath);

        var testBlocksToUpdate = new (string StartLineContains, string ReadmeLineBeforeCodeBlock)[]
        {
            (nameof(ReadMeTest_) + "()", "## Example"),
            (nameof(ReadMeTest_) + "()", "### Example - Empty"),
        };

        readmeLines = UpdateReadme(
            testSourceLines,
            readmeLines,
            testBlocksToUpdate,
            sourceStartLineOffset: 2,
            "    }",
            sourceEndLineOffset: 0,
            sourceWhitespaceToRemove: 8
        );

        var newReadme = string.Join(Environment.NewLine, readmeLines) + Environment.NewLine;
        File.WriteAllText(s_readmeFilePath, newReadme, System.Text.Encoding.UTF8);
    }

#if NET10_0
    [Test]
#endif
    public void ReadMeTest_UpdateBenchmarksInMarkdown()
    {
        if (!ShouldUpdateReadMe())
        {
            return;
        }

        if (!File.Exists(s_readmeFilePath))
        {
            return;
        }

        var readmeFilePath = s_readmeFilePath;
        var benchmarkFileNameToConfig = new Dictionary<
            string,
            (string Description, string ReadmeBefore, string ReadmeEnd, string SectionPrefix)
        >()
        {
            {
                "TestBench.md",
                ("TestBench Benchmark Results", "##### TestBench Benchmark Results", "## Example Catalogue", "######")
            },
        };

        var benchmarksDirectory = Path.Combine(s_rootDirectory, "benchmarks");
        if (!Directory.Exists(benchmarksDirectory))
        {
            return;
        }

        var processorDirectories = Directory.EnumerateDirectories(benchmarksDirectory).ToArray();
        if (processorDirectories.Length == 0)
            return;

        var readmeLines = File.ReadAllLines(readmeFilePath);

        foreach (var (fileName, config) in benchmarkFileNameToConfig)
        {
            var all = "";
            foreach (var processorDirectory in processorDirectories)
            {
                var contentsFilePath = Path.Combine(processorDirectory, fileName);
                if (!File.Exists(contentsFilePath))
                {
                    continue;
                }

                var versionsFilePath = Path.Combine(processorDirectory, "Versions.txt");
                var versions = File.ReadAllText(versionsFilePath);
                var contents = File.ReadAllText(contentsFilePath);
                var processor = processorDirectory
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Last();
                var section = $"{config.SectionPrefix}{processor} - {config.Description} ({versions})";
                var benchmarkTable = contents.Substring(contents.IndexOf('|'));
                all += $"{section}{Environment.NewLine}{Environment.NewLine}{benchmarkTable}{Environment.NewLine}";
            }

            readmeLines = ReplaceReadmeLines(
                readmeLines,
                [all],
                config.ReadmeBefore,
                config.SectionPrefix,
                0,
                config.ReadmeEnd,
                0
            );
        }

        var newReadme = string.Join(Environment.NewLine, readmeLines) + Environment.NewLine;
        File.WriteAllText(s_readmeFilePath, newReadme, System.Text.Encoding.UTF8);
    }

#if NET10_0
    [Test]
#endif
    public void ReadMeTest_PublicApi()
    {
        if (!ShouldUpdateReadMe())
        {
            return;
        }

        if (!File.Exists(s_publicApiFilePath))
        {
            return;
        }

        var publicApi = typeof(StringPoolDecoder).Assembly.GeneratePublicApi();
        var publicApiLines = File.ReadAllLines(s_publicApiFilePath);
        publicApiLines = ReplaceReadmeLines(
            publicApiLines,
            [publicApi],
            "# Public API Reference",
            "```csharp",
            1,
            "```",
            0
        );
        var newPublicApi = string.Join(Environment.NewLine, publicApiLines) + Environment.NewLine;
        File.WriteAllText(s_publicApiFilePath, newPublicApi, System.Text.Encoding.UTF8);
    }

    static bool ShouldUpdateReadMe() =>
        string.Equals(
            Environment.GetEnvironmentVariable(UpdateReadMeEnvironmentVariable),
            "1",
            StringComparison.Ordinal
        )
        || string.Equals(
            Environment.GetEnvironmentVariable(UpdateReadMeEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    static string[] UpdateReadme(
        string[] sourceLines,
        string[] readmeLines,
        (string StartLineContains, string ReadmeLineBefore)[] blocksToUpdate,
        int sourceStartLineOffset,
        string sourceEndLineStartsWith,
        int sourceEndLineOffset,
        int sourceWhitespaceToRemove,
        string readmeStartLineStartsWith = "```csharp",
        int readmeStartLineOffset = 1,
        string readmeEndLineStartsWith = "```",
        int readmeEndLineOffset = 0
    )
    {
        foreach (var (startLineContains, readmeLineBeforeBlock) in blocksToUpdate)
        {
            var sourceExampleLines = SnipLines(
                sourceLines,
                startLineContains,
                sourceStartLineOffset,
                sourceEndLineStartsWith,
                sourceEndLineOffset,
                sourceWhitespaceToRemove
            );
            readmeLines = ReplaceReadmeLines(
                readmeLines,
                sourceExampleLines,
                readmeLineBeforeBlock,
                readmeStartLineStartsWith,
                readmeStartLineOffset,
                readmeEndLineStartsWith,
                readmeEndLineOffset
            );
        }

        return readmeLines;
    }

    static string[] ReplaceReadmeLines(
        string[] readmeLines,
        string[] newLines,
        string readmeLineBeforeBlock,
        string readmeStartLineStartsWith,
        int readmeStartLineOffset,
        string readmeEndLineStartsWith,
        int readmeEndLineOffset
    )
    {
        var beforeIndex = Array.FindIndex(
            readmeLines,
            l => l.StartsWith(readmeLineBeforeBlock, StringComparison.Ordinal)
        );
        if (beforeIndex < 0)
        {
            throw new ArgumentException($"README line '{readmeLineBeforeBlock}' not found.");
        }

        var replaceStart =
            Array.FindIndex(
                readmeLines,
                beforeIndex,
                l => l.StartsWith(readmeStartLineStartsWith, StringComparison.Ordinal)
            ) + readmeStartLineOffset;
        Debug.Assert(replaceStart >= 0);
        var replaceEnd =
            Array.FindIndex(
                readmeLines,
                replaceStart,
                l => l.StartsWith(readmeEndLineStartsWith, StringComparison.Ordinal)
            ) + readmeEndLineOffset;

        return readmeLines[..replaceStart].AsEnumerable().Concat(newLines).Concat(readmeLines[replaceEnd..]).ToArray();
    }

    static string[] SnipLines(
        string[] sourceLines,
        string startLineContains,
        int startLineOffset,
        string endLineStartsWith,
        int endLineOffset,
        int whitespaceToRemove = 8
    )
    {
        var start =
            Array.FindIndex(sourceLines, l => l.Contains(startLineContains, StringComparison.Ordinal))
            + startLineOffset;
        var end =
            Array.FindIndex(sourceLines, start, l => l.StartsWith(endLineStartsWith, StringComparison.Ordinal))
            + endLineOffset;
        return sourceLines[start..end]
            .Select(l => l.Length > whitespaceToRemove ? l.Remove(0, whitespaceToRemove) : l.TrimStart())
            .ToArray();
    }

    static string SourceFile([CallerFilePath] string sourceFilePath = "") => sourceFilePath;
}
