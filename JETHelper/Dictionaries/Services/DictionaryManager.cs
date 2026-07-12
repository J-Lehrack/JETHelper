using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JETHelper.Anki.Models;
using JETHelper.Diagnostics.Services;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup;
using JETHelper.Lookup.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Coordinates dictionary discovery and the individual data loaders.
///
/// MainWindow receives card-shaped data and does not need to know which or how
/// many dictionaries supplied it. All source names are taken from the actual
/// inspected archives rather than hard-coded filenames.
/// </summary>
public sealed class DictionaryManager {
    private readonly Configuration configuration;
    private readonly DiagnosticService diagnostics;
    private readonly HashSet<string> loggedLoaderErrors = new(
              StringComparer.OrdinalIgnoreCase);

    private DictionaryCatalog catalog = null!;
    private EnglishDefinitionService englishDefinitions = null!;
    private KanjiDefinitionService kanjiDefinitions = null!;
    private JapaneseDefinitionService japaneseDefinitions = null!;
    private SlangDefinitionService slangDefinitions = null!;
    private FrequencyService frequency = null!;
    private readonly DeinflectionService deinflector = new();

    public DictionaryManager(Configuration configuration,
                             DiagnosticService diagnostics)
    {
        this.configuration = configuration;
        this.diagnostics = diagnostics;
        ReloadDictionaries();
    }

    /// <summary>
    /// Rebuilds the catalog and services so folder changes, added dictionaries,
    /// removed dictionaries, and previous load failures take effect
    /// immediately.
    /// </summary>
    public void ReloadDictionaries()
    {
        using var timing = diagnostics.Measure(
                  "Dictionaries", "Dictionary discovery and service reload");

        loggedLoaderErrors.Clear();
        catalog = new DictionaryCatalog(configuration.DictionaryFolderPath);
        englishDefinitions = new EnglishDefinitionService(catalog);
        kanjiDefinitions = new KanjiDefinitionService(catalog);
        japaneseDefinitions = new JapaneseDefinitionService(catalog);
        slangDefinitions = new SlangDefinitionService(catalog);
        frequency = new FrequencyService(catalog);

        diagnostics.Information(
                  "Dictionaries",
                  $"Catalog ready: {catalog.ReadySources.Count} usable "
                            + $"({catalog.WarningSources.Count} with warnings), "
                            + $"{catalog.ProblemSources.Count} skipped/unsupported, "
                            + $"{catalog.DuplicateDecisions.Count} duplicate decision(s), "
                            + $"{catalog.RevisionGroups.Count} multiple-revision group(s).");

        foreach (var source in catalog.Sources) {
            var summary
                      = $"{source.DisplayName}; status={source.Status}; "
                        + $"origin={source.Origin}; kinds={source.DataKinds}; "
                        + $"language={source.Language}; "
                        + $"revision={EmptyDash(source.Revision)}; "
                        + $"unreadable entries="
                        + $"{source.UnreadableEntries.Count}; "
                        + $"path={source.FilePath}";

            if (source.Status == DictionaryInspectionStatus.Ready) {
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
                                                         + ("Technical "
                                                            + "details: ")
                                                         + source.TechnicalDetails));
        }

        foreach (var decision in catalog.DuplicateDecisions) {
            diagnostics.Information(
                      "Dictionary Duplicate",
                      $"Using {decision.Preferred.DisplayName} from "
                                + $"{decision.Preferred.Origin}: {decision.Preferred.FilePath}. "
                                + $"Ignored: {string.Join(", ", decision.Ignored.Select(source => source.FilePath))}. "
                                + decision.Reason);
        }

        foreach (var group in catalog.RevisionGroups) {
            diagnostics.Warning(
                      "Dictionary Revisions",
                      $"Multiple revisions of {group.DisplayName} are loaded separately: "
                                + string.Join(
                                          ", ",
                                          group.Sources.Select(
                                                    source => $"{EmptyDash(source.Revision)} ({source.Origin}: {source.FilePath})")));
        }
    }

    public VocabularyCardData? BuildVocabularyCard(string lookupText,
                                                   string sentence)
    {
        var english = englishDefinitions.LookupExact(lookupText, maxResults: 8);
        if (english.Count == 0)
            english = englishDefinitions.Lookup(lookupText, maxResults: 8);

        var japanese = japaneseDefinitions.Lookup(lookupText, maxResults: 4);
        var slang = slangDefinitions.Lookup(lookupText, maxResults: 4);

        LogLoaderErrors();

        if (english.Count == 0 && japanese.Count == 0 && slang.Count == 0)
            return null;

        var primary = english.FirstOrDefault() ?? japanese.FirstOrDefault()
                      ?? slang.First();

        var tags = new List<string> { "jethelper" };
        AddSourceTags(tags, english);
        AddSourceTags(tags, japanese);
        AddSourceTags(tags, slang);

        var frequencyInfo = frequency.Lookup(primary.Expression);
        LogLoaderErrors();

        if (frequencyInfo.HasValue) {
            tags.Add("frequency");
            if (!string.IsNullOrWhiteSpace(frequencyInfo.Source))
                tags.Add(ToTag(frequencyInfo.Source));
        }

        return new VocabularyCardData {
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
            EnglishDefinitionSources = englishDefinitions.SourceDictionaryNames
                                                 .ToList(),
            JapaneseDefinitionSources
            = japaneseDefinitions.SourceDictionaryNames.ToList(),
            SlangDefinitionSources = slangDefinitions.SourceDictionaryNames
                                               .ToList()
        };
    }

    public IReadOnlyList<DictionarySource> DictionarySources => catalog.Sources;

    public IReadOnlyList<DictionaryDuplicateDecision>
              DuplicateDecisions => catalog.DuplicateDecisions;

    public IReadOnlyList<DictionaryRevisionGroup>
              RevisionGroups => catalog.RevisionGroups;

    public IReadOnlyList<string> LoaderErrors => GetLoadErrors();

    public KanjiCardData? BuildKanjiCard(string lookupText, string sentence)
    {
        var result = kanjiDefinitions.LookupFirstKanji(lookupText, sentence);
        LogLoaderErrors();
        return result;
    }

    public List<LookupCandidate> GetVocabularyCandidates(string lookupText)
    {
        var results
                  = englishDefinitions
                              .FindVocabularyCandidates(lookupText,
                                                        deinflector,
                                                        maxResults: 24)
                              .Concat(japaneseDefinitions
                                                .FindVocabularyCandidates(
                                                          lookupText,
                                                          deinflector,
                                                          maxResults: 24))
                              .Concat(slangDefinitions.FindVocabularyCandidates(
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

        LogLoaderErrors();
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

        if (usableDefinitionSources == 0) {
            messages.Add("No supported definition or kanji dictionaries were "
                         + "found. Add Yomitan dictionaries to "
                         + "Assets/Dictionaries or select a folder in "
                         + "/jetconfig.");
        }

        if (catalog.WarningSources.Count > 0) {
            messages.Add(
                      $"{catalog.WarningSources.Count} dictionary file(s) loaded "
                      + "with warnings because one or more archive entries "
                      + "could not "
                      + "be read. Download a fresh copy or open /jetdebug for "
                      + "details.");
        }

        if (GetLoadErrors().Count > 0) {
            messages.Add("Some dictionary banks could not be loaded. Working "
                         + "banks and "
                         + "other dictionaries remain available; open "
                         + "/jetdebug for " + "technical details.");
        }

        if (catalog.ProblemSources.Count > 0) {
            messages.Add(
                      $"{catalog.ProblemSources.Count} dictionary file(s) were "
                      + "skipped or are not yet supported. Open /jetconfig for "
                      + "details.");
        }

        return string.Join(" ", messages);
    }

    private void LogLoaderErrors()
    {
        foreach (var error in GetLoadErrors()) {
            if (!loggedLoaderErrors.Add(error))
                continue;

            diagnostics.Error(
                      "Dictionary Loader",
                      "A dictionary service could not load one or more banks. "
                                + error);
        }
    }

    private List<string> GetLoadErrors()
    {
        return new[] { englishDefinitions.LoadError,
                       japaneseDefinitions.LoadError,
                       slangDefinitions.LoadError,
                       kanjiDefinitions.LoadError,
                       frequency.LoadError }
                  .Where(error => !string.IsNullOrWhiteSpace(error))
                  .Select(error => error!)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static void
    AddSourceTags(ICollection<string> tags,
                  IEnumerable<DictionaryDefinition> definitions)
    {
        foreach (var source in definitions
                           .Select(definition => definition.SourceDictionary)
                           .Where(source => !string.IsNullOrWhiteSpace(source))
                           .Distinct(StringComparer.OrdinalIgnoreCase)) {
            tags.Add(ToTag(source));
        }
    }

    private static string ToTag(string sourceName)
    {
        var builder = new StringBuilder();

        foreach (var character in sourceName) {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static string EmptyDash(
              string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
