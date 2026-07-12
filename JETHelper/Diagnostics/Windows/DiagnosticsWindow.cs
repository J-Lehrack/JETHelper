using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Diagnostics.Windows;

/// <summary>
/// Troubleshooting window for service health, structured recent events, and
/// access to the local JETHelper.log file.
/// </summary>
public sealed class DiagnosticsWindow : Window, IDisposable {
    private readonly Plugin plugin;
    private string actionMessage = string.Empty;

    public DiagnosticsWindow(Plugin plugin) :
          base("JETHelper Diagnostics###JETHelperDiagnostics")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(650, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawLoggingSettings();
        ImGui.Separator();
        DrawDictionaryHealth();
        ImGui.Separator();
        DrawRecentEvents();
    }

    private void DrawLoggingSettings()
    {
        ImGui.TextWrapped("JETHelper records structured local diagnostics for "
                          + "troubleshooting "
                          + "and bug reports. Lookup text is excluded unless "
                            + "you explicitly "
                          + "enable it below.");
        ImGui.Spacing();

        var loggingEnabled = plugin.Configuration.DiagnosticLoggingEnabled;
        if (ImGui.Checkbox("Write JETHelper.log", ref loggingEnabled)) {
            plugin.Configuration.DiagnosticLoggingEnabled = loggingEnabled;
            plugin.Configuration.Save();
            plugin.DiagnosticService.Information(
                      "Diagnostics",
                      loggingEnabled ? "File logging enabled."
                                     : "File logging disabled. In-memory "
                                       + "diagnostic events remain available "
                                       + "for this session.");
        }

        var includeLookupText = plugin.Configuration
                                          .DiagnosticIncludeLookupText;
        if (ImGui.Checkbox("Include lookup text in diagnostics",
                           ref includeLookupText)) {
            plugin.Configuration
                      .DiagnosticIncludeLookupText = includeLookupText;
            plugin.Configuration.Save();
            plugin.DiagnosticService.Information(
                      "Diagnostics",
                      includeLookupText
                                ? "Lookup text inclusion enabled by the user."
                                : "Lookup text inclusion disabled.");
        }

        ImGui.TextDisabled("Log file:");
        ImGui.SameLine();
        ImGui.TextWrapped(plugin.DiagnosticService.LogFilePath);

        if (ImGui.Button("Open Log Folder")) {
            actionMessage = plugin.DiagnosticService.OpenLogFolder(
                                      out var error)
                                      ? "Opened the diagnostic log folder."
                                      : "Could not open the log folder: "
                                                  + error;
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy Log Path")) {
            ImGui.SetClipboardText(plugin.DiagnosticService.LogFilePath);
            actionMessage = "Copied the diagnostic log path.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Log")) {
            actionMessage = plugin.DiagnosticService.ClearLog(out var error)
                                      ? "Cleared the diagnostic log."
                                      : "Could not clear the diagnostic log: "
                                                  + error;
        }

        ImGui.TextDisabled("File logging status: "
                           + (plugin.DiagnosticService.FileLoggingOperational
                                        ? "Available"
                                        : "Unavailable for this session"));

        if (!plugin.DiagnosticService.FileLoggingOperational
            && !string.IsNullOrWhiteSpace(
                      plugin.DiagnosticService.FileLoggingFailure)) {
            ImGui.TextWrapped("File logging failed: "
                              + plugin.DiagnosticService.FileLoggingFailure
                              + ". In-memory diagnostics remain active.");
        }

        if (!string.IsNullOrWhiteSpace(actionMessage))
            ImGui.TextDisabled(actionMessage);
    }

    private void DrawDictionaryHealth()
    {
        var sources = plugin.LookupService.DictionarySources;
        var duplicates = plugin.LookupService.DictionaryDuplicateDecisions;
        var revisionGroups = plugin.LookupService.DictionaryRevisionGroups;
        var loaderErrors = plugin.LookupService.DictionaryLoaderErrors;
        var usable = sources.Count(source => source.IsUsable);
        var warnings = sources.Count(source => source.HasWarnings);
        var unsupported = sources.Count(
                  source => source.Status
                            == DictionaryInspectionStatus.Unsupported);
        var invalid = sources.Count(
                  source => source.Status
                            == DictionaryInspectionStatus.Invalid);

        var health = usable == 0 ? "Not ready"
                     : warnings > 0 || invalid > 0 || unsupported > 0
                                         || duplicates.Count > 0
                                         || revisionGroups.Count > 0
                                         || loaderErrors.Count > 0
                               ? "Ready with warnings"
                               : "Ready";

        ImGui.TextUnformatted("Dictionary service health");
        ImGui.TextDisabled($"Status: {health}");
        ImGui.TextDisabled(
                  $"Usable: {usable} | With warnings: {warnings} | "
                  + $"Unsupported: {unsupported} | Invalid: {invalid} | "
                  + $"Load errors: {loaderErrors.Count} | "
                  + $"Duplicate decisions: {duplicates.Count} | "
                  + $"Multiple-revision groups: {revisionGroups.Count}");

        if (warnings > 0) {
            ImGui.TextWrapped(
                      "One or more dictionaries are partially readable. "
                      + "JETHelper "
                      + "will use valid banks and skip unreadable entries.");
        }

        if (invalid > 0) {
            ImGui.TextWrapped("One or more dictionary archives could not be "
                              + "read. See the "
                              + "settings inventory for user-facing guidance "
                                + "and the log for "
                              + "technical details.");
        }
    }

    private void DrawRecentEvents()
    {
        if (!ImGui.TreeNodeEx("Recent diagnostic events",
                              ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var entries = plugin.DiagnosticService.RecentEntries;
        if (entries.Count == 0) {
            ImGui.TextDisabled(
                      "No diagnostic events have been recorded this session.");
            ImGui.TreePop();
            return;
        }

        ImGui.TextDisabled("Newest events appear first. The file log may "
                           + "contain more detail.");

        foreach (var entry in entries.Reverse().Take(100)) {
            ImGui.TextWrapped($"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] "
                              + $"[{entry.Category}] {entry.Message}");
            ImGui.Separator();
        }

        ImGui.TreePop();
    }
}
