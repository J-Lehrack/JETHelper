using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using JETHelper.Diagnostics.Models;

namespace JETHelper.Diagnostics.Services;

/// <summary>
/// Provides structured local diagnostics for troubleshooting and bug reports.
///
/// The normal UI should show concise, actionable messages. Technical details
/// belong here and in Dalamud's plugin log. Lookup text is excluded by default
/// and is written only when the user explicitly enables that option.
///
/// File logging is deliberately fail-safe: an unavailable or unwritable log
/// folder disables file output for the current session but never blocks the
/// plugin or the in-memory diagnostics window.
/// </summary>
public sealed class DiagnosticService : IDisposable {
    private const int MaximumRecentEntries = 250;
    private const long MaximumLogBytes = 2 * 1024 * 1024;

    private readonly object sync = new();
    private readonly Configuration configuration;
    private readonly IPluginLog pluginLog;
    private readonly List<DiagnosticEntry> recentEntries = [];

    private bool disposed;
    private bool fileLoggingOperational = true;
    private string fileLoggingFailure = string.Empty;

    public DiagnosticService(Configuration configuration,
                             IDalamudPluginInterface pluginInterface,
                             IPluginLog pluginLog)
    {
        this.configuration = configuration;
        this.pluginLog = pluginLog;

        var configDirectory = pluginInterface.ConfigDirectory.FullName;
        LogFilePath = Path.Combine(configDirectory, "JETHelper.log");
        PreviousLogFilePath = Path.Combine(configDirectory,
                                           "JETHelper.previous.log");

        try {
            Directory.CreateDirectory(configDirectory);
            RotateIfNeeded(forceSessionRotation: true);
        }
        catch (Exception ex) {
            DisableFileLoggingForSession("JETHelper could not initialize its "
                                         + "diagnostic log folder.",
                                         ex);
        }

        Information("Lifecycle",
                    $"Diagnostic session started. Plugin version: "
                              + $"{pluginInterface.Manifest.AssemblyVersion}.");
    }

    public string LogFilePath { get; }
    public string PreviousLogFilePath { get; }
    public bool FileLoggingEnabled => configuration.DiagnosticLoggingEnabled;
    public bool IncludeLookupText => configuration.DiagnosticIncludeLookupText;

    public bool FileLoggingOperational
    {
        get {
            lock (sync) return fileLoggingOperational;
        }
    }

    public string FileLoggingFailure
    {
        get {
            lock (sync) return fileLoggingFailure;
        }
    }

    public IReadOnlyList<DiagnosticEntry> RecentEntries
    {
        get {
            lock (sync) return recentEntries.ToList();
        }
    }

    public void
    Information(string category,
                string message) => Write(DiagnosticLevel.Information,
                                         category,
                                         message);

    public void Warning(string category,
                        string message) => Write(DiagnosticLevel.Warning,
                                                 category,
                                                 message);

    public void
    Error(string category, string message, Exception? exception = null)
    {
        var detail = exception is null
                               ? message
                               : message + Environment.NewLine + exception;

        Write(DiagnosticLevel.Error, category, detail);
    }

    /// <summary>
    /// Logs how long an operation took when the returned scope is disposed.
    /// "Finished" is intentionally neutral: callers that need success/failure
    /// semantics should log the outcome explicitly.
    /// </summary>
    public IDisposable
    Measure(string category,
            string operation,
            string? details = null) => new TimedOperation(this,
                                                          category,
                                                          operation,
                                                          details);

    public bool ClearLog(out string? errorMessage)
    {
        lock (sync) recentEntries.Clear();

        try {
            var folder = Path.GetDirectoryName(LogFilePath)
                         ?? throw new InvalidOperationException(
                                   "The diagnostic log folder could not be "
                                   + "determined.");

            Directory.CreateDirectory(folder);
            File.WriteAllText(LogFilePath, string.Empty);

            lock (sync)
            {
                fileLoggingOperational = true;
                fileLoggingFailure = string.Empty;
            }

            errorMessage = null;
            Information("Diagnostics", "Diagnostic log cleared by the user.");
            return true;
        }
        catch (Exception ex) {
            errorMessage = ex.Message;
            DisableFileLoggingForSession(
                      "JETHelper could not clear its diagnostic log.", ex);
            return false;
        }
    }

    public bool OpenLogFolder(out string? errorMessage)
    {
        try {
            var folder = Path.GetDirectoryName(LogFilePath)
                         ?? throw new InvalidOperationException(
                                   "The diagnostic log folder could not be "
                                   + "determined.");

            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder,
                                                 UseShellExecute = true });

            errorMessage = null;
            return true;
        }
        catch (Exception ex) {
            errorMessage = ex.Message;
            Error("Diagnostics",
                  "Could not open the diagnostic log folder.",
                  ex);
            return false;
        }
    }

    public string DescribeLookupInput(string cleanedText)
    {
        if (!configuration.DiagnosticIncludeLookupText) {
            return $"characters={cleanedText.Length}; "
                   + "text omitted by privacy setting";
        }

        var oneLine = cleanedText.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (oneLine.Length > 160)
            oneLine = oneLine[..160] + "…";

        return $"characters={cleanedText.Length}; text=\"{oneLine}\"";
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Information("Lifecycle", "Diagnostic session ended.");
        disposed = true;
    }

    private void Write(DiagnosticLevel level, string category, string message)
    {
        if (disposed)
            return;

        var normalizedCategory = string.IsNullOrWhiteSpace(category)
                                           ? "General"
                                           : category.Trim();
        var normalizedMessage = string.IsNullOrWhiteSpace(message)
                                          ? "(no details)"
                                          : message.Trim();
        var entry = new DiagnosticEntry(DateTimeOffset.Now,
                                        level,
                                        normalizedCategory,
                                        normalizedMessage);

        Exception? fileWriteException = null;

        lock (sync)
        {
            AddRecentEntry(entry);

            if (configuration.DiagnosticLoggingEnabled
                && fileLoggingOperational) {
                try {
                    RotateIfNeeded(forceSessionRotation: false);
                    File.AppendAllText(LogFilePath, Format(entry));
                }
                catch (Exception ex) {
                    fileLoggingOperational = false;
                    fileLoggingFailure = ex.Message;
                    fileWriteException = ex;

                    AddRecentEntry(new DiagnosticEntry(
                              DateTimeOffset.Now,
                              DiagnosticLevel.Warning,
                              "Diagnostics",
                              "File logging was disabled for this session "
                              + "because "
                                        + "JETHelper.log could not be written. "
                                          + "In-memory "
                                        + "diagnostics remain available."));
                }
            }
        }

        if (fileWriteException is not null) {
            SafePluginLogError(fileWriteException,
                               "[Diagnostics] Could not write JETHelper.log. "
                                         + "File logging was disabled for this "
                                           + "session.");
        }

        MirrorToDalamud(level, normalizedCategory, normalizedMessage);
    }

    private void AddRecentEntry(DiagnosticEntry entry)
    {
        recentEntries.Add(entry);

        if (recentEntries.Count > MaximumRecentEntries) {
            recentEntries.RemoveRange(
                      0, recentEntries.Count - MaximumRecentEntries);
        }
    }

    private void DisableFileLoggingForSession(string message,
                                              Exception exception)
    {
        lock (sync)
        {
            fileLoggingOperational = false;
            fileLoggingFailure = exception.Message;

            AddRecentEntry(new DiagnosticEntry(
                      DateTimeOffset.Now,
                      DiagnosticLevel.Warning,
                      "Diagnostics",
                      message
                                + (" File logging is unavailable for this "
                                   + "session; ")
                                + "in-memory diagnostics remain active."));
        }

        SafePluginLogError(exception, "[Diagnostics] " + message);
    }

    private void
    MirrorToDalamud(DiagnosticLevel level, string category, string message)
    {
        var dalamudMessage = $"[{category}] {message}";

        try {
            switch (level) {
                case DiagnosticLevel.Warning:
                    pluginLog.Warning("{Message}", dalamudMessage);
                    break;
                case DiagnosticLevel.Error:
                    pluginLog.Error("{Message}", dalamudMessage);
                    break;
                default:
                    // Informational detail is kept in JETHelper.log and the
                    // diagnostics window. Dalamud's shared log receives only
                    // warnings and errors to avoid unnecessary noise.
                    break;
            }
        }
        catch {
            // Diagnostics must never become a second source of plugin failure.
        }
    }

    private void SafePluginLogError(Exception exception, string message)
    {
        try {
            pluginLog.Error(exception, message);
        }
        catch {
            // The in-memory list is still available even if Dalamud logging
            // itself is unavailable.
        }
    }

    private void RotateIfNeeded(bool forceSessionRotation)
    {
        if (!File.Exists(LogFilePath))
            return;

        var length = new FileInfo(LogFilePath).Length;
        if (!forceSessionRotation && length < MaximumLogBytes)
            return;

        if (length == 0)
            return;

        File.Move(LogFilePath, PreviousLogFilePath, overwrite: true);
    }

    private static string Format(DiagnosticEntry entry)
    {
        var message = entry.Message.Replace("\r\n", "\n")
                                .Replace('\r', '\n')
                                .Replace("\n", Environment.NewLine + "    ");

        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} "
               + $"[{entry.Level}] [{entry.Category}] {message}"
               + Environment.NewLine;
    }

    private sealed class TimedOperation : IDisposable {
        private readonly DiagnosticService diagnostics;
        private readonly string category;
        private readonly string operation;
        private readonly string? details;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private bool finished;

        public TimedOperation(DiagnosticService diagnostics,
                              string category,
                              string operation,
                              string? details)
        {
            this.diagnostics = diagnostics;
            this.category = category;
            this.operation = operation;
            this.details = details;
        }

        public void Dispose()
        {
            if (finished)
                return;

            finished = true;
            stopwatch.Stop();

            var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty
                                                            : $"; {details}";

            diagnostics.Information(
                      category,
                      $"{operation} finished in "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms{suffix}.");
        }
    }
}
