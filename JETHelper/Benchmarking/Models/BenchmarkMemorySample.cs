using System;

namespace JETHelper.Benchmarking.Models;

/// <summary>
/// Process-wide and managed-runtime memory measurements captured at a named
/// dictionary benchmark stage.
/// </summary>
public sealed class BenchmarkMemorySample
{
    public DateTimeOffset Timestamp { get; init; }
    public string Stage { get; init; } = string.Empty;
    public long ManagedBytes { get; init; }
    public long ManagedHeapBytes { get; init; }
    public long FragmentedBytes { get; init; }
    public long TotalAvailableMemoryBytes { get; init; }
    public long MemoryLoadBytes { get; init; }
    public long HighMemoryLoadThresholdBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
}
