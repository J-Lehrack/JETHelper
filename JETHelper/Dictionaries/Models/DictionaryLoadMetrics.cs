namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Captures retained-data and timing measurements produced by one real
/// JETHelper dictionary loader for one inspected source.
///
/// These values describe JETHelper's runtime behavior rather than raw archive
/// facts. Temporary allocations are measured separately by the benchmark
/// service's process and managed-memory sampler.
/// </summary>
public sealed class DictionaryLoadMetrics
{
    public string ServiceName { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int BanksDiscovered { get; init; }
    public int BanksProcessed { get; init; }
    public int BanksSkipped { get; init; }
    public long RowsProcessed { get; init; }
    public int LookupKeysAdded { get; init; }
    public int StoredResultObjectsAdded { get; init; }
    public int ErrorCount { get; init; }
    public double DurationMilliseconds { get; init; }
}
