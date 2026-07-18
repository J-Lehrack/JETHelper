using System;

namespace JETHelper.Benchmarking.Models;

/// <summary>
/// Concise terminal information for the most recent deliberate benchmark run.
/// Detailed source, loader, and memory records are written to the JSONL file.
/// </summary>
public sealed class DictionaryBenchmarkRunSummary
{
    public string RunId { get; init; } = string.Empty;
    public string ProfileLabel { get; init; } = string.Empty;
    public string Trigger { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public double DurationMilliseconds { get; init; }
    public int? ReloadGeneration { get; init; }
    public long PeakManagedBytes { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long PeakPrivateMemoryBytes { get; init; }
    public string Message { get; init; } = string.Empty;
}
