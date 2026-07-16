using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JETHelper.Anki.Models;
using JETHelper.Diagnostics.Services;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Inspection;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup;
using JETHelper.Lookup.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Coordinates dictionary discovery and the individual data loaders.
///
/// Discovery and archive inspection run on a background worker. The currently
/// active dictionary snapshot remains available until a complete replacement
/// snapshot is ready, so manual reloads do not interrupt normal lookup use.
///
/// Parsing and index construction inside the individual dictionary services are
/// still lazy in this phase and will move to background preloading in Phase
/// 6B.2C.
/// </summary>
public sealed class DictionaryManager : IDisposable
{
    private readonly Configuration configuration;
    private readonly DiagnosticService diagnostics;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim reloadGate = new(1, 1);
    private readonly HashSet<string> loggedLoaderErrors = new(
              StringComparer.OrdinalIgnoreCase);
    private readonly DeinflectionService deinflector = new();

    private CancellationTokenSource? reloadCancellation;
    private DictionaryRuntimeSnapshot? activeSnapshot;
    private DictionaryReloadStatus reloadStatus = new();
    private int reloadGeneration;
    private bool disposed;

    public DictionaryManager(Configuration configuration,
                             DiagnosticService diagnostics)
    {
        this.configuration = configuration;
        this.diagnostics = diagnostics;

        // Startup discovery must not block plugin construction or the game/UI
        // thread. Until the first snapshot is ready, lookups receive a concise
        // "still loading" status instead of synchronously loading dictionaries.
        ReloadDictionaries();
    }

    /// <summary>
    /// Requests a background dictionary reload and returns immediately.
    ///
    /// A newer request cancels an older request. The semaphore ensures only one
    /// discovery/inspection worker performs expensive archive work at a time,
    /// even if cancellation cannot interrupt the middle of one ZIP entry read.
    /// </summary>
    public void ReloadDictionaries()
    {
        CancellationTokenSource cancellation;
        int generation;
        string configuredPath;
        bool hasActiveSnapshot;

        lock (stateLock)
        {
            if (disposed)
                return;

            reloadCancellation?.Cancel();

            cancellation = new CancellationTokenSource();
            reloadCancellation = cancellation;
            generation = ++reloadGeneration;
            configuredPath = configuration.DictionaryFolderPath;
            hasActiveSnapshot = activeSnapshot is not null;

            reloadStatus = new DictionaryReloadStatus
            {
                Stage = DictionaryReloadStage.Discovering,
                Message = hasActiveSnapshot
                              ? "Discovering replacement dictionary sources. "
                                + "The current dictionary snapshot remains active."
                              : "Discovering dictionary sources.",
                HasActiveSnapshot = hasActiveSnapshot
            };
        }

        diagnostics.Information(
                  "Dictionaries",
                  $"Dictionary reload requested; generation={generation}; "
                            + $"configured path={EmptyDash(configuredPath)}; "
                            + $"existing snapshot={hasActiveSnapshot}.");

        // Task.Run is intentional here. ZIP discovery, decompression, checksum
        // validation, and JSON parsing are blocking/CPU work rather than
        // naturally asynchronous I/O.
        _ = Task.Run(() => ReloadWorker(
                           generation,
                           configuredPath,
                           cancellation));
    }

    public DictionaryReloadStatus ReloadStatus
    {
        get
        {
            lock (stateLock)
                return reloadStatus;
        }
    }

    public IReadOnlyList<DictionarySource> DictionarySources
        => GetSnapshot()?.Catalog.Sources ?? [];

    public IReadOnlyList<DictionaryDuplicateDecision> DuplicateDecisions
        => GetSnapshot()?.Catalog.DuplicateDecisions ?? [];

    public IReadOnlyList<DictionaryRevisionGroup> RevisionGroups
        => GetSnapshot()?.Catalog.RevisionGroups ?? [];

    public IReadOnlyList<string> LoaderErrors
        => GetSnapshot() is { } snapshot ? GetLoadErrors(snapshot) : [];

    public VocabularyCardData? BuildVocabularyCard(string lookupText,
                                                   string sentence)
    {
        var snapshot = GetSnapshot();
        if (snapshot is null)
            return null;

        var english = snapshot.EnglishDefinitions.LookupExact(
                  lookupText,
                  maxResults: 8);
        if (english.Count == 0)
            english = snapshot.EnglishDefinitions.Lookup(
                      lookupText,
                      maxResults: 8);

        var japanese = snapshot.JapaneseDefinitions.Lookup(
                  lookupText,
                  maxResults: 4);
        var slang = snapshot.SlangDefinitions.Lookup(
                  lookupText,
                  maxResults: 4);

        LogLoaderErrors(snapshot);

        if (english.Count == 0 && japanese.Count == 0 && slang.Count == 0)
            return null;

        var primary = english.FirstOrDefault() ?? japanese.FirstOrDefault()
                      ?? slang.First();

        var tags = new List<string> { "jethelper" };
        AddSourceTags(tags, english);
        AddSourceTags(tags, japanese);
        AddSourceTags(tags, slang);

        var frequencyInfo = snapshot.Frequency.Lookup(primary.Expression);
        LogLoaderErrors(snapshot);

        if (frequencyInfo.HasValue)
        {
            tags.Add("frequency");
            if (!string.IsNullOrWhiteSpace(frequencyInfo.Source))
                tags.Add(ToTag(frequencyInfo.Source));
        }

        return new VocabularyCardData
        {
            Expression = primary.Expression,
            Furigana = string.IsNullOrWhiteSpace(primary.Reading)
                                 ? primary.Expression
                                 : primary.Reading,
            EnglishDefinitions = english,
            JapaneseDefinitions = japanese,
            SlangDefinitions = slang,
            Frequency = frequencyInfo,
            Sentence = sentence,
            PitchAccent = new PitchAccentInfo(),
            Tags = tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList(),
            EnglishDefinitionSources = snapshot.EnglishDefinitions
                                                .SourceDictionaryNames
                                                .ToList(),
            JapaneseDefinitionSources = snapshot.JapaneseDefinitions
                                                 .SourceDictionaryNames
                                                 .ToList(),
            SlangDefinitionSources = snapshot.SlangDefinitions
                                              .SourceDictionaryNames
                                              .ToList()
        };
    }

    public KanjiCardData? BuildKanjiCard(string lookupText, string sentence)
    {
        var snapshot = GetSnapshot();
        if (snapshot is null)
            return null;

        var result = snapshot.KanjiDefinitions.LookupFirstKanji(
                  lookupText,
                  sentence);
        LogLoaderErrors(snapshot);
        return result;
    }

    public List<LookupCandidate> GetVocabularyCandidates(string lookupText)
    {
        var snapshot = GetSnapshot();
        if (snapshot is null)
            return [];

        var results
                  = snapshot.EnglishDefinitions
                            .FindVocabularyCandidates(
                                      lookupText,
                                      deinflector,
                                      maxResults: 24)
                            .Concat(snapshot.JapaneseDefinitions
                                              .FindVocabularyCandidates(
                                                        lookupText,
                                                        deinflector,
                                                        maxResults: 24))
                            .Concat(snapshot.SlangDefinitions
                                              .FindVocabularyCandidates(
                                                        lookupText,
                                                        deinflector,
                                                        maxResults: 24))
                            .OrderBy(candidate => candidate.StartIndex)
                            .ThenByDescending(
                                      candidate => candidate.SurfaceLength)
                            .ThenBy(candidate => candidate.IsDeinflected ? 0
                                                                         : 1)
                            .GroupBy(candidate => candidate.Text,
                                     StringComparer.Ordinal)
                            .Select(group => group.First())
                            .Take(24)
                            .ToList();

        LogLoaderErrors(snapshot);
        return results;
    }

    public List<string> GetAdditionalKanjiCandidates(string lookupText)
    {
        var kanji = JapaneseText.ExtractUniqueKanji(lookupText);
        return kanji.Count <= 1 ? [] : kanji[1..];
    }

    /// <summary>
    /// Returns concise user-facing setup/load information. Technical parser
    /// details are written to diagnostics instead of being appended to lookups.
    /// </summary>
    public string GetDictionaryStatusMessage()
    {
        var snapshot = GetSnapshot();
        var status = ReloadStatus;

        if (snapshot is null)
        {
            if (status.IsActive)
            {
                return "Dictionaries are still loading in the background. "
                       + "Try the lookup again after the dictionary status in "
                       + "/jetconfig reports Ready.";
            }

            if (status.Stage == DictionaryReloadStage.Failed)
            {
                return "Dictionary loading failed. Open /jetconfig or "
                       + "/jetdebug for details, then retry the reload.";
            }

            if (status.Stage == DictionaryReloadStage.Cancelled)
                return "Dictionary loading was cancelled. Reload dictionaries "
                       + "from /jetconfig.";

            return "No dictionary snapshot is available yet.";
        }

        var catalog = snapshot.Catalog;
        var messages = new List<string>();
        var usableDefinitionSources
                  = catalog.Select(DictionaryDataKind.TermDefinitions,
                                   DictionaryLanguageKind.English,
                                   DictionaryContentRole.General)
                              .Count
                    + catalog.Select(DictionaryDataKind.TermDefinitions,
                                     DictionaryLanguageKind.Mixed,
                                     DictionaryContentRole.General)
                                .Count
                    + catalog.Select(DictionaryDataKind.TermDefinitions,
                                     DictionaryLanguageKind.Japanese,
                                     DictionaryContentRole.General)
                                .Count
                    + catalog.Select(DictionaryDataKind.TermDefinitions,
                                     language: null,
                                     role: DictionaryContentRole.SlangOrMedia)
                                .Count
                    + catalog.Select(DictionaryDataKind.KanjiDefinitions).Count;

        if (usableDefinitionSources == 0)
        {
            messages.Add("No supported definition or kanji dictionaries were "
                         + "found. Add Yomitan dictionaries to "
                         + "Assets/Dictionaries or select a folder in "
                         + "/jetconfig.");
        }

        if (catalog.WarningSources.Count > 0)
        {
            messages.Add(
                      $"{catalog.WarningSources.Count} dictionary file(s) loaded "
                      + "with warnings because one or more archive entries "
                      + "could not be read. Download a fresh copy or open "
                      + "/jetdebug for details.");
        }

        if (GetLoadErrors(snapshot).Count > 0)
        {
            messages.Add("Some dictionary banks could not be loaded. Working "
                         + "banks and other dictionaries remain available; open "
                         + "/jetdebug for technical details.");
        }

        if (catalog.ProblemSources.Count > 0)
        {
            messages.Add(
                      $"{catalog.ProblemSources.Count} dictionary file(s) were "
                      + "skipped or are not yet supported. Open /jetconfig for "
                      + "details.");
        }

        return string.Join(" ", messages);
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed)
                return;

            disposed = true;
            reloadCancellation?.Cancel();
        }
    }

    private void ReloadWorker(int generation,
                              string configuredPath,
                              CancellationTokenSource cancellation)
    {
        var stopwatch = Stopwatch.StartNew();
        var token = cancellation.Token;
        var gateEntered = false;

        try
        {
            reloadGate.Wait(token);
            gateEntered = true;
            token.ThrowIfCancellationRequested();

            UpdateReloadStatus(
                      generation,
                      new DictionaryReloadStatus
                      {
                          Stage = DictionaryReloadStage.Discovering,
                          Message = GetSnapshot() is null
                                            ? "Discovering dictionary sources."
                                            : "Discovering replacement dictionary "
                                              + "sources. The current snapshot "
                                              + "remains active.",
                          HasActiveSnapshot = GetSnapshot() is not null
                      });

            var candidates = DictionaryPathResolver
                      .FindDictionaryZipCandidates(configuredPath);
            token.ThrowIfCancellationRequested();

            UpdateReloadStatus(
                      generation,
                      new DictionaryReloadStatus
                      {
                          Stage = DictionaryReloadStage.Validating,
                          TotalSources = candidates.Count,
                          Message = candidates.Count == 0
                                            ? "No dictionary ZIP files were found."
                                            : $"Validating {candidates.Count} "
                                              + "dictionary source(s).",
                          HasActiveSnapshot = GetSnapshot() is not null
                      });

            var inspected = new List<DictionarySource>(candidates.Count);

            for (var index = 0; index < candidates.Count; index++)
            {
                token.ThrowIfCancellationRequested();

                var candidate = candidates[index];
                var filename = System.IO.Path.GetFileName(candidate.FilePath);

                UpdateReloadStatus(
                          generation,
                          new DictionaryReloadStatus
                          {
                              Stage = DictionaryReloadStage.Validating,
                              ProcessedSources = index,
                              TotalSources = candidates.Count,
                              CurrentSource = filename,
                              Message = $"Validating dictionary {index + 1} of "
                                        + $"{candidates.Count}.",
                              HasActiveSnapshot = GetSnapshot() is not null
                          });

                inspected.Add(YomitanDictionaryInspector.Inspect(
                          candidate.FilePath,
                          candidate.Origin));

                token.ThrowIfCancellationRequested();

                UpdateReloadStatus(
                          generation,
                          new DictionaryReloadStatus
                          {
                              Stage = DictionaryReloadStage.Validating,
                              ProcessedSources = index + 1,
                              TotalSources = candidates.Count,
                              CurrentSource = filename,
                              Message = $"Validated {index + 1} of "
                                        + $"{candidates.Count} dictionary "
                                          + "source(s).",
                              HasActiveSnapshot = GetSnapshot() is not null
                          });
            }

            var catalog = DictionaryCatalog.FromInspectedSources(inspected);
            var replacement = new DictionaryRuntimeSnapshot(catalog);
            token.ThrowIfCancellationRequested();

            var hasWarnings = catalog.WarningSources.Count > 0
                              || catalog.ProblemSources.Count > 0
                              || catalog.DuplicateDecisions.Count > 0
                              || catalog.RevisionGroups.Count > 0;

            lock (stateLock)
            {
                if (disposed || generation != reloadGeneration)
                    return;

                activeSnapshot = replacement;
                loggedLoaderErrors.Clear();
                reloadStatus = new DictionaryReloadStatus
                {
                    Stage = hasWarnings
                                  ? DictionaryReloadStage.ReadyWithWarnings
                                  : DictionaryReloadStage.Ready,
                    ProcessedSources = candidates.Count,
                    TotalSources = candidates.Count,
                    Message = BuildReadyMessage(catalog),
                    HasActiveSnapshot = true
                };
            }

            stopwatch.Stop();
            LogCatalog(replacement.Catalog, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            UpdateReloadStatus(
                      generation,
                      new DictionaryReloadStatus
                      {
                          Stage = DictionaryReloadStage.Cancelled,
                          Message = GetSnapshot() is null
                                            ? "Dictionary loading was cancelled."
                                            : "Replacement dictionary loading was "
                                              + "cancelled. The previous snapshot "
                                              + "remains active.",
                          HasActiveSnapshot = GetSnapshot() is not null
                      });

            diagnostics.Information(
                      "Dictionaries",
                      $"Dictionary reload generation {generation} was cancelled.");
        }
        catch (Exception ex)
        {
            UpdateReloadStatus(
                      generation,
                      new DictionaryReloadStatus
                      {
                          Stage = DictionaryReloadStage.Failed,
                          Message = GetSnapshot() is null
                                            ? "Dictionary loading failed. Open "
                                              + "/jetdebug for technical details."
                                            : "Replacement dictionary loading "
                                              + "failed. The previous snapshot "
                                              + "remains active.",
                          HasActiveSnapshot = GetSnapshot() is not null
                      });

            diagnostics.Error(
                      "Dictionaries",
                      $"Dictionary reload generation {generation} failed. "
                                + "The previous snapshot was preserved when "
                                  + "available.",
                      ex);
        }
        finally
        {
            if (gateEntered)
                reloadGate.Release();

            lock (stateLock)
            {
                if (ReferenceEquals(reloadCancellation, cancellation))
                    reloadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void UpdateReloadStatus(int generation,
                                    DictionaryReloadStatus status)
    {
        lock (stateLock)
        {
            if (disposed || generation != reloadGeneration)
                return;

            reloadStatus = status;
        }
    }

    private DictionaryRuntimeSnapshot? GetSnapshot()
    {
        lock (stateLock)
            return activeSnapshot;
    }

    private void LogCatalog(DictionaryCatalog catalog, TimeSpan elapsed)
    {
        diagnostics.Information(
                  "Dictionaries",
                  $"Catalog ready in {elapsed.TotalMilliseconds:F1} ms: "
                            + $"{catalog.ReadySources.Count} usable "
                            + $"({catalog.WarningSources.Count} with warnings), "
                            + $"{catalog.ProblemSources.Count} "
                              + "skipped/unsupported, "
                            + $"{catalog.DuplicateDecisions.Count} duplicate "
                              + "decision(s), "
                            + $"{catalog.RevisionGroups.Count} "
                              + "multiple-revision group(s).");

        foreach (var source in catalog.Sources)
        {
            var summary
                      = $"{source.DisplayName}; status={source.Status}; "
                        + $"origin={source.Origin}; kinds={source.DataKinds}; "
                        + $"language={source.Language}; "
                        + $"revision={EmptyDash(source.Revision)}; "
                        + $"unreadable entries="
                        + $"{source.UnreadableEntries.Count}; "
                        + $"path={source.FilePath}";

            if (source.Status == DictionaryInspectionStatus.Ready)
            {
                diagnostics.Information("Dictionary Source", summary);
                continue;
            }

            diagnostics.Warning(
                      "Dictionary Source",
                      summary + Environment.NewLine + source.ErrorMessage
                                + (string.IsNullOrWhiteSpace(
                                             source.TechnicalDetails)
                                             ? string.Empty
                                             : Environment.NewLine
                                                         + "Technical details: "
                                                         + source.TechnicalDetails));
        }

        foreach (var decision in catalog.DuplicateDecisions)
        {
            diagnostics.Information(
                      "Dictionary Duplicate",
                      $"Using {decision.Preferred.DisplayName} from "
                                + $"{decision.Preferred.Origin}: "
                                + $"{decision.Preferred.FilePath}. Ignored: "
                                + $"{string.Join(", ", decision.Ignored.Select(source => source.FilePath))}. "
                                + decision.Reason);
        }

        foreach (var group in catalog.RevisionGroups)
        {
            diagnostics.Warning(
                      "Dictionary Revisions",
                      $"Multiple revisions of {group.DisplayName} are loaded "
                                + "separately: "
                                + string.Join(
                                          ", ",
                                          group.Sources.Select(
                                                    source => $"{EmptyDash(source.Revision)} "
                                                              + $"({source.Origin}: "
                                                              + $"{source.FilePath})")));
        }
    }

    private void LogLoaderErrors(DictionaryRuntimeSnapshot snapshot)
    {
        foreach (var error in GetLoadErrors(snapshot))
        {
            var shouldLog = false;

            lock (stateLock)
                shouldLog = loggedLoaderErrors.Add(error);

            if (!shouldLog)
                continue;

            diagnostics.Error(
                      "Dictionary Loader",
                      "A dictionary service could not load one or more banks. "
                                + error);
        }
    }

    private static List<string> GetLoadErrors(
              DictionaryRuntimeSnapshot snapshot)
    {
        return new[]
               {
                   snapshot.EnglishDefinitions.LoadError,
                   snapshot.JapaneseDefinitions.LoadError,
                   snapshot.SlangDefinitions.LoadError,
                   snapshot.KanjiDefinitions.LoadError,
                   snapshot.Frequency.LoadError
               }
              .Where(error => !string.IsNullOrWhiteSpace(error))
              .Select(error => error!)
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .ToList();
    }

    private static string BuildReadyMessage(DictionaryCatalog catalog)
    {
        return $"Dictionary catalog ready: {catalog.ReadySources.Count} usable, "
               + $"{catalog.WarningSources.Count} with warnings, "
               + $"{catalog.ProblemSources.Count} skipped/unsupported.";
    }

    private static void AddSourceTags(
              ICollection<string> tags,
              IEnumerable<DictionaryDefinition> definitions)
    {
        foreach (var source in definitions
                           .Select(definition => definition.SourceDictionary)
                           .Where(source => !string.IsNullOrWhiteSpace(source))
                           .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(ToTag(source));
        }
    }

    private static string ToTag(string sourceName)
    {
        var builder = new StringBuilder();

        foreach (var character in sourceName)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static string EmptyDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private sealed class DictionaryRuntimeSnapshot
    {
        public DictionaryRuntimeSnapshot(DictionaryCatalog catalog)
        {
            Catalog = catalog;
            EnglishDefinitions = new EnglishDefinitionService(catalog);
            KanjiDefinitions = new KanjiDefinitionService(catalog);
            JapaneseDefinitions = new JapaneseDefinitionService(catalog);
            SlangDefinitions = new SlangDefinitionService(catalog);
            Frequency = new FrequencyService(catalog);
        }

        public DictionaryCatalog Catalog { get; }
        public EnglishDefinitionService EnglishDefinitions { get; }
        public KanjiDefinitionService KanjiDefinitions { get; }
        public JapaneseDefinitionService JapaneseDefinitions { get; }
        public SlangDefinitionService SlangDefinitions { get; }
        public FrequencyService Frequency { get; }
    }
}
