using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Maple.StringPool.ComparisonBenchmarks;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams)]
[BenchmarkCategory("0")]
public class TestBench
{
    [Params(25_000)]
    public int Count { get; set; }

    [Benchmark(Baseline = true)]
    public string Maple_StringPool______()
    {
        // Baseline: placeholder until a real binary is available in CI.
        return string.Empty;
    }
}
