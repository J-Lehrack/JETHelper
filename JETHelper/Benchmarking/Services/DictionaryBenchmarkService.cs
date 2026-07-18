using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using JETHelper.Benchmarking.Models;
using JETHelper.Diagnostics.Services;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Benchmarking.Services;

/// <summary>
/// Opt-in development instrumentation for dictionary validation, indexing, and
/// snapshot replacement benchmarks.
///
/// Normal plugin behavior is unchanged while no run is active. A deliberate
/// benchmark writes newline-delimited JSON records to the Dalamud plugin
/// configuration directory and samples process/managed memory only for the
/// duration of that one reload.
/// </summary>
public sealed class DictionaryBenchmarkService : IDisposable
{
    private const int MemorySampleIntervalMilliseconds = 250;

    private readonly object sync = new();
    private readonly DiagnosticService diagnostics;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private ActiveBenchmarkRun? activeRun;
    private DictionaryBenchmarkRunSummary? lastRunSummary;
    private WeakReference? lastReplacedSnapshot;
    private bool disposed;
    private bool outputOperational = true;
    private string outputFailure = string.Empty;

    public DictionaryBenchmarkService(
        IDalamudPluginInterface pluginInterface,
        DiagnosticService diagnostics)
    {
        this.diagnostics = diagnostics;

        var configDirectory = pluginInterface.ConfigDirectory.FullName;
        OutputFilePath = Path.Combine(
            configDirectory,
            "JETHelper.dictionary-benchmark.jsonl");

        try
        {
            Directory.CreateDirectory(configDirectory);
        }
        catch (Exception ex)
        {
            outputOperational = false;
            outputFailure = ex.Message;
            diagnostics.Error(
                "Benchmark",
                "JETHelper could not initialize the dictionary benchmark "
                + "output folder.",
                ex);
        }
    }

    public string OutputFilePath { get; }

    public bool IsRunActive
    {
        get
        {
            lock (sync)
                return activeRun is not null;
        }
    }

    public string ActiveProfileLabel
    {
        get
        {
            lock (sync)
                return activeRun?.ProfileLabel ?? string.Empty;
        }
    }

    public string ActiveRunId
    {
        get
        {
            lock (sync)
                return activeRun?.RunId ?? string.Empty;
        }
    }

    public DictionaryBenchmarkRunSummary? LastRunSummary
    {
        get
        {
            lock (sync)
                return lastRunSummary;
        }
    }

    public bool OutputOperational
    {
        get
        {
            lock (sync)
                return outputOperational;
        }
    }

    public string OutputFailure
    {
        get
        {
            lock (sync)
                return outputFailure;
        }
    }

    public bool CanCapturePostCollection
    {
        get
        {
            lock (sync)
                return lastRunSummary is not null;
        }
    }

    public bool IsTrackingReload(int generation)
    {
        lock (sync)
        {
            return activeRun is not null
                   && activeRun.ReloadGeneration == generation;
        }
    }

    /// <summary>
    /// Arms one benchmark run. The next dictionary reload attaches to this run
    /// and automatically ends it with a completed, cancelled, or failed result.
    /// </summary>
    public bool StartRun(
        string profileLabel,
        string trigger,
        bool collectGarbageBeforeStart,
        out string message)
    {
        profileLabel = NormalizeLabel(profileLabel, "unnamed-profile");
        trigger = NormalizeLabel(trigger, "manual");

        lock (sync)
        {
            if (disposed)
            {
                message = "Benchmark instrumentation has been disposed.";
                return false;
            }

            if (activeRun is not null)
            {
                message = "A dictionary benchmark run is already active.";
                return false;
            }

            activeRun = new ActiveBenchmarkRun(
                Guid.NewGuid().ToString("N"),
                profileLabel,
                trigger,
                DateTimeOffset.Now);
        }

        if (collectGarbageBeforeStart)
            CollectFullGarbage();

        ActiveBenchmarkRun run;
        lock (sync)
            run = activeRun!;

        var baseline = CaptureMemory("run_baseline");
        UpdatePeak(run, baseline);

        WriteEvent(
            run,
            "run_started",
            new
            {
                collect_garbage_before_start = collectGarbageBeforeStart,
                environment = new
                {
                    os = RuntimeInformation.OSDescription,
                    framework = RuntimeInformation.FrameworkDescription,
                    process_architecture = RuntimeInformation.ProcessArchitecture
                        .ToString(),
                    processor_count = Environment.ProcessorCount,
                    server_gc = GCSettings.IsServerGC
                },
                memory = baseline
            });

        diagnostics.Information(
            "Benchmark",
            $"Dictionary benchmark run {run.RunId} armed for profile "
            + $"{run.ProfileLabel}; trigger={run.Trigger}.");

        message = "Benchmark run armed. Starting the dictionary reload will "
                  + "attach it to this run.";
        return true;
    }

    /// <summary>
    /// Associates the next dictionary reload generation with the armed run.
    /// Calls are no-ops when benchmarking is disabled.
    /// </summary>
    public void AttachReload(
        int generation,
        string configuredPath,
        bool hasActiveSnapshot)
    {
        ActiveBenchmarkRun? run;

        lock (sync)
        {
            run = activeRun;
            if (run is null || run.ReloadGeneration is not null)
                return;

            run.ReloadGeneration = generation;
            run.ReloadStopwatch.Start();
        }

        var memory = CaptureMemory("before_discovery");
        UpdatePeak(run, memory);
        StartMemorySampler(run);

        WriteEvent(
            run,
            "reload_started",
            new
            {
                generation,
                configured_path = configuredPath,
                had_active_snapshot = hasActiveSnapshot,
                memory
            });
    }

    public void RecordDiscoveryCompleted(
        int generation,
        int sourceCount,
        TimeSpan duration)
    {
        if (!TryGetRun(generation, out var run))
            return;

        var memory = CaptureMemory("after_discovery");
        UpdatePeak(run, memory);

        WriteEvent(
            run,
            "discovery_completed",
            new
            {
                generation,
                source_count = sourceCount,
                duration_ms = duration.TotalMilliseconds,
                memory
            });
    }

    public void RecordValidationSource(
        int generation,
        DictionarySource source,
        TimeSpan duration)
    {
        if (!TryGetRun(generation, out var run))
            return;

        long compressedBytes;
        try
        {
            compressedBytes = new FileInfo(source.FilePath).Length;
        }
        catch
        {
            compressedBytes = -1;
        }

        WriteEvent(
            run,
            "validation_source",
            new
            {
                generation,
                source_name = source.DisplayName,
                source_path = source.FilePath,
                origin = source.Origin.ToString(),
                status = source.Status.ToString(),
                data_kinds = source.DataKinds.ToString(),
                language = source.Language.ToString(),
                compressed_bytes = compressedBytes,
                unreadable_entry_count = source.UnreadableEntries.Count,
                duration_ms = duration.TotalMilliseconds
            });
    }

    public void RecordValidationCompleted(
        int generation,
        int sourceCount,
        TimeSpan duration)
    {
        if (!TryGetRun(generation, out var run))
            return;

        var memory = CaptureMemory("after_validation");
        UpdatePeak(run, memory);

        WriteEvent(
            run,
            "validation_completed",
            new
            {
                generation,
                source_count = sourceCount,
                duration_ms = duration.TotalMilliseconds,
                memory
            });
    }

    public void RecordIndexingCompleted(
        int generation,
        IReadOnlyList<DictionaryLoadMetrics> metrics,
        TimeSpan duration)
    {
        if (!TryGetRun(generation, out var run))
            return;

        foreach (var metric in metrics)
        {
            WriteEvent(
                run,
                "indexing_source",
                new
                {
                    generation,
                    service_name = metric.ServiceName,
                    source_name = metric.SourceName,
                    source_path = metric.SourcePath,
                    banks_discovered = metric.BanksDiscovered,
                    banks_processed = metric.BanksProcessed,
                    banks_skipped = metric.BanksSkipped,
                    rows_processed = metric.RowsProcessed,
                    lookup_keys_added = metric.LookupKeysAdded,
                    stored_result_objects_added = metric.StoredResultObjectsAdded,
                    error_count = metric.ErrorCount,
                    duration_ms = metric.DurationMilliseconds
                });
        }

        foreach (var group in metrics.GroupBy(
                     metric => metric.ServiceName,
                     StringComparer.OrdinalIgnoreCase))
        {
            WriteEvent(
                run,
                "indexing_service_summary",
                new
                {
                    generation,
                    service_name = group.Key,
                    source_count = group.Count(),
                    banks_discovered = group.Sum(metric => metric.BanksDiscovered),
                    banks_processed = group.Sum(metric => metric.BanksProcessed),
                    banks_skipped = group.Sum(metric => metric.BanksSkipped),
                    rows_processed = group.Sum(metric => metric.RowsProcessed),
                    lookup_key_count = group.Sum(metric => metric.LookupKeysAdded),
                    stored_result_object_count = group.Sum(
                        metric => metric.StoredResultObjectsAdded),
                    error_count = group.Sum(metric => metric.ErrorCount),
                    source_duration_ms = group.Sum(
                        metric => metric.DurationMilliseconds)
                });
        }

        var memory = CaptureMemory("after_indexing_before_activation");
        UpdatePeak(run, memory);

        WriteEvent(
            run,
            "indexing_completed",
            new
            {
                generation,
                source_metric_count = metrics.Count,
                banks_processed = metrics.Sum(metric => metric.BanksProcessed),
                rows_processed = metrics.Sum(metric => metric.RowsProcessed),
                lookup_key_count = metrics.Sum(metric => metric.LookupKeysAdded),
                stored_result_object_count = metrics.Sum(
                    metric => metric.StoredResultObjectsAdded),
                duration_ms = duration.TotalMilliseconds,
                memory
            });
    }

    public void RecordActivation(
        int generation,
        WeakReference? replacedSnapshot)
    {
        if (!TryGetRun(generation, out var run))
            return;

        var memory = CaptureMemory("after_activation");
        UpdatePeak(run, memory);

        lock (sync)
        {
            run.ReplacedSnapshot = replacedSnapshot;
            lastReplacedSnapshot = replacedSnapshot;
        }

        WriteEvent(
            run,
            "snapshot_activated",
            new
            {
                generation,
                replaced_existing_snapshot = replacedSnapshot is not null,
                memory
            });
    }

    public void CompleteReload(
        int generation,
        string outcome,
        string message)
    {
        if (!TryGetRun(generation, out var run))
            return;

        run.ReloadStopwatch.Stop();
        StopMemorySampler(run);

        var terminalMemory = CaptureMemory("reload_terminal");
        UpdatePeak(run, terminalMemory);
        var peakMemory = GetPeak(run);

        WriteEvent(
            run,
            "memory_peak",
            new
            {
                generation,
                sample_interval_ms = MemorySampleIntervalMilliseconds,
                sample_count = run.MemorySampleCount,
                peak = peakMemory
            });

        WriteEvent(
            run,
            "run_completed",
            new
            {
                generation,
                outcome,
                message,
                duration_ms = run.ReloadStopwatch.Elapsed.TotalMilliseconds,
                terminal_memory = terminalMemory
            });

        var summary = new DictionaryBenchmarkRunSummary
        {
            RunId = run.RunId,
            ProfileLabel = run.ProfileLabel,
            Trigger = run.Trigger,
            Outcome = outcome,
            StartedAt = run.StartedAt,
            FinishedAt = DateTimeOffset.Now,
            DurationMilliseconds = run.ReloadStopwatch.Elapsed.TotalMilliseconds,
            ReloadGeneration = generation,
            PeakManagedBytes = peakMemory.ManagedBytes,
            PeakWorkingSetBytes = peakMemory.WorkingSetBytes,
            PeakPrivateMemoryBytes = peakMemory.PrivateMemoryBytes,
            Message = message
        };

        lock (sync)
        {
            if (ReferenceEquals(activeRun, run))
                activeRun = null;

            lastRunSummary = summary;
            lastReplacedSnapshot = run.ReplacedSnapshot;
        }

        diagnostics.Information(
            "Benchmark",
            $"Dictionary benchmark run {run.RunId} finished with outcome "
            + $"{outcome} in {run.ReloadStopwatch.Elapsed.TotalMilliseconds:F1} ms. "
            + $"Peak managed={FormatBytes(peakMemory.ManagedBytes)}; "
            + $"working set={FormatBytes(peakMemory.WorkingSetBytes)}; "
            + $"private={FormatBytes(peakMemory.PrivateMemoryBytes)}.");
    }

    /// <summary>
    /// Explicitly forces a full collection after a completed run. This is a
    /// development-only action because it can briefly hitch the game. It helps
    /// distinguish replacement-snapshot memory from memory that remains live.
    /// </summary>
    public bool CapturePostCollection(out string message)
    {
        DictionaryBenchmarkRunSummary? summary;
        WeakReference? replacedSnapshot;

        lock (sync)
        {
            if (activeRun is not null)
            {
                message = "Wait for the active benchmark reload to finish.";
                return false;
            }

            summary = lastRunSummary;
            replacedSnapshot = lastReplacedSnapshot;
        }

        if (summary is null)
        {
            message = "No completed benchmark run is available.";
            return false;
        }

        CollectFullGarbage();
        var memory = CaptureMemory("post_forced_collection");

        WriteEvent(
            summary,
            "post_collection_memory",
            new
            {
                generation = summary.ReloadGeneration,
                old_snapshot_alive = replacedSnapshot?.IsAlive,
                memory
            });

        message = "Captured post-collection memory. The forced collection may "
                  + "have caused a brief development-only hitch.";
        return true;
    }

    public bool ClearOutput(out string message)
    {
        lock (sync)
        {
            if (activeRun is not null)
            {
                message = "The benchmark output cannot be cleared during an "
                          + "active run.";
                return false;
            }
        }

        try
        {
            File.WriteAllText(OutputFilePath, string.Empty);

            lock (sync)
            {
                outputOperational = true;
                outputFailure = string.Empty;
                lastRunSummary = null;
                lastReplacedSnapshot = null;
            }

            message = "Cleared the dictionary benchmark output.";
            return true;
        }
        catch (Exception ex)
        {
            DisableOutput(ex);
            message = "Could not clear benchmark output: " + ex.Message;
            return false;
        }
    }

    public bool OpenOutputFolder(out string message)
    {
        try
        {
            var folder = Path.GetDirectoryName(OutputFilePath)
                         ?? throw new InvalidOperationException(
                             "The benchmark output folder could not be "
                             + "determined.");

            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });

            message = "Opened the benchmark output folder.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Could not open the benchmark output folder: "
                      + ex.Message;
            diagnostics.Error(
                "Benchmark",
                "Could not open the dictionary benchmark output folder.",
                ex);
            return false;
        }
    }

    public void Dispose()
    {
        ActiveBenchmarkRun? run;

        lock (sync)
        {
            if (disposed)
                return;

            run = activeRun;
            activeRun = null;
        }

        if (run is not null)
        {
            StopMemorySampler(run);
            WriteEvent(
                run,
                "run_aborted",
                new
                {
                    outcome = "plugin_disposed",
                    message = "Plugin disposal ended the benchmark before the "
                              + "reload produced a terminal outcome."
                });
            diagnostics.Information(
                "Benchmark",
                $"Dictionary benchmark run {run.RunId} ended during plugin "
                + "disposal before a terminal reload outcome was recorded.");
        }

        lock (sync)
            disposed = true;
    }

    private bool TryGetRun(
        int generation,
        out ActiveBenchmarkRun run)
    {
        lock (sync)
        {
            if (activeRun is not null
                && activeRun.ReloadGeneration == generation)
            {
                run = activeRun;
                return true;
            }
        }

        run = null!;
        return false;
    }

    private void StartMemorySampler(ActiveBenchmarkRun run)
    {
        run.MemorySamplerCancellation = new CancellationTokenSource();
        var token = run.MemorySamplerCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var sample = CaptureMemory("interval_sample");
                    UpdatePeak(run, sample);
                    Interlocked.Increment(ref run.MemorySampleCount);
                    await Task.Delay(
                        MemorySampleIntervalMilliseconds,
                        token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the benchmark reload reaches a terminal state.
            }
            catch (Exception ex)
            {
                diagnostics.Error(
                    "Benchmark",
                    "The dictionary benchmark memory sampler failed.",
                    ex);
            }
            finally
            {
                run.MemorySamplerCancellation?.Dispose();
            }
        });
    }

    private static void StopMemorySampler(ActiveBenchmarkRun run)
    {
        try
        {
            run.MemorySamplerCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The sampler may already have ended and disposed its token source.
        }
    }

    private static void UpdatePeak(
        ActiveBenchmarkRun run,
        BenchmarkMemorySample sample)
    {
        lock (run.PeakLock)
        {
            run.PeakMemory = new BenchmarkMemorySample
            {
                Timestamp = sample.Timestamp,
                Stage = "observed_peak",
                ManagedBytes = Math.Max(
                    run.PeakMemory.ManagedBytes,
                    sample.ManagedBytes),
                ManagedHeapBytes = Math.Max(
                    run.PeakMemory.ManagedHeapBytes,
                    sample.ManagedHeapBytes),
                FragmentedBytes = Math.Max(
                    run.PeakMemory.FragmentedBytes,
                    sample.FragmentedBytes),
                TotalAvailableMemoryBytes = Math.Max(
                    run.PeakMemory.TotalAvailableMemoryBytes,
                    sample.TotalAvailableMemoryBytes),
                MemoryLoadBytes = Math.Max(
                    run.PeakMemory.MemoryLoadBytes,
                    sample.MemoryLoadBytes),
                HighMemoryLoadThresholdBytes = Math.Max(
                    run.PeakMemory.HighMemoryLoadThresholdBytes,
                    sample.HighMemoryLoadThresholdBytes),
                WorkingSetBytes = Math.Max(
                    run.PeakMemory.WorkingSetBytes,
                    sample.WorkingSetBytes),
                PrivateMemoryBytes = Math.Max(
                    run.PeakMemory.PrivateMemoryBytes,
                    sample.PrivateMemoryBytes),
                Gen0Collections = Math.Max(
                    run.PeakMemory.Gen0Collections,
                    sample.Gen0Collections),
                Gen1Collections = Math.Max(
                    run.PeakMemory.Gen1Collections,
                    sample.Gen1Collections),
                Gen2Collections = Math.Max(
                    run.PeakMemory.Gen2Collections,
                    sample.Gen2Collections)
            };
        }
    }

    private static BenchmarkMemorySample GetPeak(ActiveBenchmarkRun run)
    {
        lock (run.PeakLock)
            return run.PeakMemory;
    }

    private static BenchmarkMemorySample CaptureMemory(string stage)
    {
        long managedBytes = 0;
        long managedHeapBytes = 0;
        long fragmentedBytes = 0;
        long totalAvailableMemoryBytes = 0;
        long memoryLoadBytes = 0;
        long highMemoryLoadThresholdBytes = 0;
        long workingSetBytes = 0;
        long privateMemoryBytes = 0;
        var gen0Collections = 0;
        var gen1Collections = 0;
        var gen2Collections = 0;

        // Benchmark instrumentation must never become a dependency of normal
        // dictionary loading. If one platform/runtime counter is unavailable,
        // preserve the remaining measurements and emit zero for that field.
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            managedBytes = GC.GetTotalMemory(forceFullCollection: false);
            managedHeapBytes = gcInfo.HeapSizeBytes;
            fragmentedBytes = gcInfo.FragmentedBytes;
            totalAvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes;
            memoryLoadBytes = gcInfo.MemoryLoadBytes;
            highMemoryLoadThresholdBytes
                = gcInfo.HighMemoryLoadThresholdBytes;
            gen0Collections = GC.CollectionCount(0);
            gen1Collections = GC.CollectionCount(1);
            gen2Collections = GC.CollectionCount(2);
        }
        catch
        {
            // Leave unavailable managed-runtime counters at zero.
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            workingSetBytes = process.WorkingSet64;
            privateMemoryBytes = process.PrivateMemorySize64;
        }
        catch
        {
            // Leave unavailable process counters at zero.
        }

        return new BenchmarkMemorySample
        {
            Timestamp = DateTimeOffset.Now,
            Stage = stage,
            ManagedBytes = managedBytes,
            ManagedHeapBytes = managedHeapBytes,
            FragmentedBytes = fragmentedBytes,
            TotalAvailableMemoryBytes = totalAvailableMemoryBytes,
            MemoryLoadBytes = memoryLoadBytes,
            HighMemoryLoadThresholdBytes = highMemoryLoadThresholdBytes,
            WorkingSetBytes = workingSetBytes,
            PrivateMemoryBytes = privateMemoryBytes,
            Gen0Collections = gen0Collections,
            Gen1Collections = gen1Collections,
            Gen2Collections = gen2Collections
        };
    }

    private static void CollectFullGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void WriteEvent(
        ActiveBenchmarkRun run,
        string eventType,
        object data)
    {
        WriteEnvelope(
            run.RunId,
            run.ProfileLabel,
            run.Trigger,
            run.ReloadGeneration,
            eventType,
            data);
    }

    private void WriteEvent(
        DictionaryBenchmarkRunSummary summary,
        string eventType,
        object data)
    {
        WriteEnvelope(
            summary.RunId,
            summary.ProfileLabel,
            summary.Trigger,
            summary.ReloadGeneration,
            eventType,
            data);
    }

    private void WriteEnvelope(
        string runId,
        string profileLabel,
        string trigger,
        int? generation,
        string eventType,
        object data)
    {
        lock (sync)
        {
            if (!outputOperational || disposed)
                return;

            try
            {
                var envelope = new
                {
                    schema_version = 1,
                    timestamp = DateTimeOffset.Now,
                    run_id = runId,
                    profile = profileLabel,
                    trigger,
                    reload_generation = generation,
                    event_type = eventType,
                    data
                };

                var line = JsonSerializer.Serialize(envelope, jsonOptions)
                           + Environment.NewLine;
                File.AppendAllText(OutputFilePath, line);
            }
            catch (Exception ex)
            {
                DisableOutput(ex);
            }
        }
    }

    private void DisableOutput(Exception exception)
    {
        lock (sync)
        {
            outputOperational = false;
            outputFailure = exception.Message;
        }

        diagnostics.Error(
            "Benchmark",
            "Dictionary benchmark file output was disabled for this session. "
            + "Normal plugin behavior remains available.",
            exception);
    }

    private static string NormalizeLabel(
        string value,
        string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

        return normalized.Length <= 80
            ? normalized
            : normalized[..80];
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        var kib = bytes / 1024d;
        if (kib < 1024)
            return $"{kib:F1} KiB";

        var mib = kib / 1024d;
        if (mib < 1024)
            return $"{mib:F1} MiB";

        return $"{mib / 1024d:F2} GiB";
    }

    private sealed class ActiveBenchmarkRun
    {
        public ActiveBenchmarkRun(
            string runId,
            string profileLabel,
            string trigger,
            DateTimeOffset startedAt)
        {
            RunId = runId;
            ProfileLabel = profileLabel;
            Trigger = trigger;
            StartedAt = startedAt;
        }

        public string RunId { get; }
        public string ProfileLabel { get; }
        public string Trigger { get; }
        public DateTimeOffset StartedAt { get; }
        public int? ReloadGeneration { get; set; }
        public Stopwatch ReloadStopwatch { get; } = new();
        public object PeakLock { get; } = new();
        public BenchmarkMemorySample PeakMemory { get; set; } = new();
        public CancellationTokenSource? MemorySamplerCancellation { get; set; }
        public int MemorySampleCount;
        public WeakReference? ReplacedSnapshot { get; set; }
    }
}
