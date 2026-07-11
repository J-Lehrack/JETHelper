using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JETHelper.Dictionaries;
using JETHelper.Models;
using JETHelper.Utils;

namespace JETHelper.Services;

/// <summary>
/// Coordinates dictionary discovery and the individual data loaders.
///
/// MainWindow receives card-shaped data and does not need to know which or how
/// many dictionaries supplied it. All source names are taken from the actual
/// inspected archives rather than hard-coded filenames.
/// </summary>
public sealed class DictionaryManager
{
    private readonly Configuration configuration;
    private DictionaryCatalog catalog = null!;
    private EnglishDefinitionService englishDefinitions = null!;
    private KanjiDefinitionService kanjiDefinitions = null!;
    private JapaneseDefinitionService japaneseDefinitions = null!;
    private SlangDefinitionService slangDefinitions = null!;
    private FrequencyService frequency = null!;
    private readonly DeinflectionService deinflector = new();

    public DictionaryManager(Configuration configuration)
    {
        this.configuration = configuration;
        ReloadDictionaries();
    }

    /// <summary>
    /// Rebuilds the catalog and services so folder changes, added dictionaries,
    /// removed dictionaries, and previous load failures take effect immediately.
    /// </summary>
    public void ReloadDictionaries()
    {
        catalog = new DictionaryCatalog(configuration.DictionaryFolderPath);
        englishDefinitions = new EnglishDefinitionService(catalog);
        kanjiDefinitions = new KanjiDefinitionService(catalog);
        japaneseDefinitions = new JapaneseDefinitionService(catalog);
        slangDefinitions = new SlangDefinitionService(catalog);
        frequency = new FrequencyService(catalog);
    }

    public VocabularyCardData? BuildVocabularyCard(
        string lookupText,
        string sentence)
    {
        var english = englishDefinitions.LookupExact(
            lookupText,
            maxResults: 8);
        if (english.Count == 0)
            english = englishDefinitions.Lookup(lookupText, maxResults: 8);

        var japanese = japaneseDefinitions.Lookup(lookupText, maxResults: 4);
        var slang = slangDefinitions.Lookup(lookupText, maxResults: 4);

        if (english.Count == 0
            && japanese.Count == 0
            && slang.Count == 0)
            return null;

        var primary = english.FirstOrDefault()
                      ?? japanese.FirstOrDefault()
                      ?? slang.First();

        var tags = new List<string> { "jethelper" };
        AddSourceTags(tags, english);
        AddSourceTags(tags, japanese);
        AddSourceTags(tags, slang);

        var frequencyInfo = frequency.Lookup(primary.Expression);
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
            Tags = tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EnglishDefinitionSources = englishDefinitions
                .SourceDictionaryNames
                .ToList(),
            JapaneseDefinitionSources = japaneseDefinitions
                .SourceDictionaryNames
                .ToList(),
            SlangDefinitionSources = slangDefinitions
                .SourceDictionaryNames
                .ToList()
        };
    }

    public IReadOnlyList<DictionarySource> DictionarySources
        => catalog.Sources;

    public KanjiCardData? BuildKanjiCard(
        string lookupText,
        string sentence)
        => kanjiDefinitions.LookupFirstKanji(lookupText, sentence);

    public List<LookupCandidate> GetVocabularyCandidates(string lookupText)
    {
        return englishDefinitions.FindVocabularyCandidates(
                lookupText,
                deinflector,
                maxResults: 24)
            .Concat(japaneseDefinitions.FindVocabularyCandidates(
                lookupText,
                deinflector,
                maxResults: 24))
            .Concat(slangDefinitions.FindVocabularyCandidates(
                lookupText,
                deinflector,
                maxResults: 24))
            .OrderBy(candidate => candidate.StartIndex)
            .ThenByDescending(candidate => candidate.SurfaceLength)
            .ThenBy(candidate => candidate.IsDeinflected ? 0 : 1)
            .GroupBy(candidate => candidate.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(24)
            .ToList();
    }

    public List<string> GetAdditionalKanjiCandidates(string lookupText)
    {
        var kanji = JapaneseText.ExtractUniqueKanji(lookupText);
        return kanji.Count <= 1 ? [] : kanji[1..];
    }

    /// <summary>
    /// Returns concise user-facing setup/load information. Individual broken or
    /// unsupported archives never prevent successfully loaded sources from being
    /// used.
    /// </summary>
    public string GetDictionaryStatusMessage()
    {
        var messages = new List<string>();
        var usableDefinitionSources = catalog.Select(
                DictionaryDataKind.TermDefinitions,
                DictionaryLanguageKind.English,
                DictionaryContentRole.General).Count
            + catalog.Select(
                DictionaryDataKind.TermDefinitions,
                DictionaryLanguageKind.Mixed,
                DictionaryContentRole.General).Count
            + catalog.Select(
                DictionaryDataKind.TermDefinitions,
                DictionaryLanguageKind.Japanese,
                DictionaryContentRole.General).Count
            + catalog.Select(
                DictionaryDataKind.TermDefinitions,
                language: null,
                role: DictionaryContentRole.SlangOrMedia).Count
            + catalog.Select(DictionaryDataKind.KanjiDefinitions).Count;

        if (usableDefinitionSources == 0)
        {
            messages.Add(
                "No supported definition or kanji dictionaries were found. Add Yomitan dictionaries to Assets/Dictionaries or select a folder in /jetconfig.");
        }

        var loadErrors = new[]
            {
                englishDefinitions.LoadError,
                japaneseDefinitions.LoadError,
                slangDefinitions.LoadError,
                kanjiDefinitions.LoadError,
                frequency.LoadError
            }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (loadErrors.Count > 0)
        {
            messages.Add("Some dictionary sources could not be loaded: "
                         + string.Join("; ", loadErrors.Take(3)));
        }

        var problemSources = catalog.ProblemSources;
        if (problemSources.Count > 0)
        {
            var summaries = problemSources
                .Take(3)
                .Select(source =>
                    $"{source.DisplayName} ({source.Status}: {source.ErrorMessage})");

            messages.Add("Some dictionary files were skipped: "
                         + string.Join("; ", summaries));
        }

        return string.Join(" ", messages);
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
}
