using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace JETHelper.Benchmarking.Windows;

/// <summary>
/// Development-only controls for deliberate, one-reload dictionary benchmark
/// runs. Normal users never need to open this window.
/// </summary>
public sealed class DictionaryBenchmarkWindow : Window, IDisposable {
    private readonly Plugin plugin;
    private string profileLabel = "curated-bundled-baseline";
    private bool collectGarbageBeforeRun = true;
    private bool cancellationRequested;
    private string actionMessage = string.Empty;

    public DictionaryBenchmarkWindow(Plugin plugin) :
          base("JETHelper Dictionary Benchmark###JETHelperDictionaryBenchmark")
    {
        this.plugin = plugin;
        profileLabel = plugin.Configuration.DictionaryBenchmarkProfileLabel;
        collectGarbageBeforeRun
                  = plugin.Configuration
                              .DictionaryBenchmarkCollectGarbageBeforeStart;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(680, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped(
                  "These development tools measure JETHelper's real dictionary "
                  + "validation, indexing, retained-object counts, and "
                  + "process/managed "
                  + "memory. Instrumentation is inactive until a deliberate "
                  + "benchmark " + "run is started.");
        ImGui.Spacing();

        DrawRunControls();
        ImGui.Separator();
        DrawCurrentState();
        ImGui.Separator();
        DrawOutputControls();
        ImGui.Separator();
        DrawLastRun();
    }

    private void DrawRunControls()
    {
        ImGui.TextUnformatted("Benchmark profile");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##DictionaryBenchmarkProfile", ref profileLabel, 80);

        ImGui.TextDisabled(
                  "Use stable labels such as curated-bundled-baseline, "
                  + "future-expanded, or large-personal.");

        ImGui.Checkbox("Force a full garbage collection before the run",
                       ref collectGarbageBeforeRun);
        ImGui.TextWrapped(
                  "A forced collection improves baseline repeatability but may "
                  + "cause "
                  + "a brief development-only hitch before loading begins.");

        var reloadStatus = plugin.LookupService.DictionaryReloadStatus;
        var cannotStart = plugin.DictionaryBenchmarkService.IsRunActive
                          || reloadStatus.IsActive;

        if (cannotStart)
            ImGui.BeginDisabled();

        if (ImGui.Button("Start Benchmark Reload")) {
            plugin.Configuration.DictionaryBenchmarkProfileLabel
                      = string.IsNullOrWhiteSpace(profileLabel)
                                  ? "unnamed-profile"
                                  : profileLabel.Trim();
            plugin.Configuration.DictionaryBenchmarkCollectGarbageBeforeStart
                      = collectGarbageBeforeRun;
            plugin.Configuration.Save();

            if (plugin.DictionaryBenchmarkService.StartRun(
                          plugin.Configuration.DictionaryBenchmarkProfileLabel,
                          "manual-reload",
                          collectGarbageBeforeRun,
                          out actionMessage)) {
                plugin.LookupService.ReloadDictionaries();
                actionMessage
                          = "Benchmark reload started. Keep FFXIV and other "
                            + "plugin activity as stable as practical.";
            }
        }

        if (cannotStart)
            ImGui.EndDisabled();

        if (!plugin.DictionaryBenchmarkService.IsRunActive
            || !reloadStatus.IsActive) {
            cancellationRequested = false;
        }

        var canCancel = plugin.DictionaryBenchmarkService.IsRunActive
                        && reloadStatus.IsActive && !cancellationRequested;

        ImGui.SameLine();
        if (!canCancel)
            ImGui.BeginDisabled();

        if (ImGui.Button("Cancel Active Benchmark")) {
            if (plugin.LookupService.CancelDictionaryReload(out actionMessage))
                cancellationRequested = true;
        }

        if (!canCancel)
            ImGui.EndDisabled();

        ImGui.TextDisabled(
                  "Cancellation is development-only. It preserves the "
                  + "currently "
                  + "active snapshot and ends this benchmark with a cancelled "
                  + "terminal outcome once the worker reaches a cancellation "
                  + "check.");

        if (ImGui.Button("Arm Next Plugin Startup")) {
            plugin.Configuration.DictionaryBenchmarkProfileLabel
                      = string.IsNullOrWhiteSpace(profileLabel)
                                  ? "unnamed-profile"
                                  : profileLabel.Trim();
            plugin.Configuration.DictionaryBenchmarkNextStartup = true;
            plugin.Configuration.DictionaryBenchmarkCollectGarbageBeforeStart
                      = collectGarbageBeforeRun;
            plugin.Configuration.Save();
            actionMessage
                      = "The next JETHelper plugin startup is armed for one "
                        + "dictionary benchmark run. Disable and re-enable "
                        + "the plugin when ready.";
        }

        if (plugin.Configuration.DictionaryBenchmarkNextStartup) {
            ImGui.TextDisabled(
                      "Next startup armed: "
                      + plugin.Configuration.DictionaryBenchmarkProfileLabel);

            if (ImGui.Button("Disarm Next Plugin Startup")) {
                plugin.Configuration.DictionaryBenchmarkNextStartup = false;
                plugin.Configuration.Save();
                actionMessage = "The next-startup benchmark was disarmed.";
            }
        }

        var canCapturePostCollection
                  = plugin.DictionaryBenchmarkService.CanCapturePostCollection
                    && !plugin.DictionaryBenchmarkService.IsRunActive
                    && !reloadStatus.IsActive;

        if (!canCapturePostCollection)
            ImGui.BeginDisabled();

        if (ImGui.Button("Capture Post-GC Memory")) {
            plugin.DictionaryBenchmarkService.CapturePostCollection(
                      out actionMessage);
        }

        if (!canCapturePostCollection)
            ImGui.EndDisabled();

        ImGui.TextWrapped("Post-GC capture explicitly forces a full collection "
                          + "after a run. "
                          + "Use it only for benchmarks; it can momentarily "
                          + "hitch the game.");

        if (!string.IsNullOrWhiteSpace(actionMessage))
            ImGui.TextDisabled(actionMessage);
    }

    private void DrawCurrentState()
    {
        var benchmark = plugin.DictionaryBenchmarkService;
        var reload = plugin.LookupService.DictionaryReloadStatus;

        ImGui.TextUnformatted("Current state");
        ImGui.TextDisabled(
                  benchmark.IsRunActive
                            ? $"Benchmark active: {benchmark.ActiveProfileLabel} "
                                        + $"({benchmark.ActiveRunId})"
                            : "Benchmark instrumentation: idle");

        ImGui.TextDisabled($"Dictionary stage: {reload.Stage}");
        if (!string.IsNullOrWhiteSpace(reload.CurrentSource))
            ImGui.TextDisabled("Current source/group: " + reload.CurrentSource);

        if (reload.TotalSources > 0) {
            ImGui.TextDisabled(
                      $"Progress: {reload.ProcessedSources}/{reload.TotalSources}");
        }

        if (!string.IsNullOrWhiteSpace(reload.Message))
            ImGui.TextWrapped(reload.Message);
    }

    private void DrawOutputControls()
    {
        var benchmark = plugin.DictionaryBenchmarkService;

        ImGui.TextUnformatted("Structured output");
        ImGui.TextWrapped(benchmark.OutputFilePath);
        ImGui.TextDisabled(
                  "The JSONL file contains no lookup text. Each line is one "
                  + "structured "
                  + "run, source, service, memory-stage, or terminal event.");

        if (ImGui.Button("Open Output Folder"))
            benchmark.OpenOutputFolder(out actionMessage);

        ImGui.SameLine();
        if (ImGui.Button("Copy Output Path")) {
            ImGui.SetClipboardText(benchmark.OutputFilePath);
            actionMessage = "Copied the benchmark output path.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Output"))
            benchmark.ClearOutput(out actionMessage);

        ImGui.TextDisabled("Output status: "
                           + (benchmark.OutputOperational
                                        ? "Available"
                                        : "Unavailable for this session"));

        if (!benchmark.OutputOperational
            && !string.IsNullOrWhiteSpace(benchmark.OutputFailure)) {
            ImGui.TextWrapped("Benchmark output failed: "
                              + benchmark.OutputFailure);
        }
    }

    private void DrawLastRun()
    {
        var summary = plugin.DictionaryBenchmarkService.LastRunSummary;

        ImGui.TextUnformatted("Most recent run");
        if (summary is null) {
            ImGui.TextDisabled("No benchmark run has completed this session.");
            return;
        }

        ImGui.TextDisabled($"Profile: {summary.ProfileLabel}");
        ImGui.TextDisabled($"Trigger: {summary.Trigger}");
        ImGui.TextDisabled($"Outcome: {summary.Outcome}");
        ImGui.TextDisabled($"Duration: {summary.DurationMilliseconds:F1} ms");
        ImGui.TextDisabled(
                  $"Peak managed: {FormatBytes(summary.PeakManagedBytes)}");
        ImGui.TextDisabled(
                  $"Peak working set: {FormatBytes(summary.PeakWorkingSetBytes)}");
        ImGui.TextDisabled($"Peak private memory: "
                           + FormatBytes(summary.PeakPrivateMemoryBytes));
        ImGui.TextWrapped(summary.Message);
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
}
