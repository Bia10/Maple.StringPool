using BenchmarkDotNet.Attributes;

namespace Maple.StringPool.Benchmarks;

public class StringPoolDecoderBench
{
    [Benchmark]
    public string GetString_Slot25()
    {
        // NOTE: Replace with real benchmark setup using a test binary or stub image.
        // This placeholder exercises the infrastructure only.
        return "Hero";
    }
}
