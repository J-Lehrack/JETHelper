using System.Collections.Generic;
using System.Linq;
using JETHelper.Anki.Models;
using JETHelper.Diagnostics.Services;
using JETHelper.Dictionaries.Models;
using JETHelper.Dictionaries.Services;
using JETHelper.Lookup.Models;

namespace JETHelper.Lookup.Services;

/// <summary>
/// LookupService is the main text-processing service.
///
/// It owns the lookup pipeline:
/// raw input -> cleaned text -> Japanese extraction -> dictionary/card-shaped
/// data.
/// </summary>
public sealed class LookupService : System.IDisposable
{
    private readonly Configuration configuration;
    private readonly DiagnosticService diagnostics;
    private readonly DictionaryManager dictionaryManager;

    public LookupService(Configuration configuration,
                         DiagnosticService diagnostics)
    {
        this.configuration = configuration;
        this.diagnostics = diagnostics;
        dictionaryManager = new DictionaryManager(configuration, diagnostics);
    }

    public void ReloadDictionaries() => dictionaryManager.ReloadDictionaries();

    public DictionaryReloadStatus
              DictionaryReloadStatus => dictionaryManager.ReloadStatus;

    public IReadOnlyList<DictionarySource>
              DictionarySources => dictionaryManager.DictionarySources;

    public IReadOnlyList<DictionaryDuplicateDecision>
              DictionaryDuplicateDecisions => dictionaryManager
                                                        .DuplicateDecisions;

    public IReadOnlyList<DictionaryRevisionGroup>
              DictionaryRevisionGroups => dictionaryManager.RevisionGroups;

    public IReadOnlyList<string>
              DictionaryLoaderErrors => dictionaryManager.LoaderErrors;

    public LookupResult ProcessRawText(string? rawText,
                                       string source = "Unknown")
    {
        var cleaned = JapaneseText.CleanForLookup(rawText);
        using var timing = diagnostics.Measure(
                  "Lookup",
                  "Text lookup",
                  $"source={source}; {diagnostics.DescribeLookupInput(cleaned)}");

        var kind = JapaneseText.Classify(cleaned);
        var containsJapanese = JapaneseText.ContainsJapanese(cleaned);
        var hasNonJapaneseContent = JapaneseText.HasNonJapaneseLookupContent(
                  cleaned);

        var lookupText = containsJapanese
                                   ? JapaneseText.ExtractJapaneseForLookup(
                                               cleaned)
                                   : string.Empty;

        VocabularyCardData? vocabularyCard = null;
        KanjiCardData? kanjiCard = null;
        var vocabularyCandidateDetails = new List<LookupCandidate>();
        var vocabularyCandidates = new List<string>();
        var additionalKanji = new List<string>();

        if (containsJapanese && !string.IsNullOrWhiteSpace(lookupText))
        {
            // Collect vocabulary candidates from the original lookup text
            // first. The default vocabulary card uses the first dictionary
            // match in reading order, while buttons let the user change focus
            // without losing the copied sentence/context.
            vocabularyCandidateDetails
                      = dictionaryManager.GetVocabularyCandidates(lookupText);
            vocabularyCandidates
                      = vocabularyCandidateDetails
                                  .Select(candidate => candidate.Text)
                                  .Distinct()
                                  .ToList();
            var primaryVocabularyLookup
                      = vocabularyCandidateDetails.FirstOrDefault()?.Text
                        ?? lookupText;

            vocabularyCard = dictionaryManager.BuildVocabularyCard(
                      primaryVocabularyLookup, cleaned);
            kanjiCard = dictionaryManager.BuildKanjiCard(lookupText, cleaned);
            additionalKanji = dictionaryManager.GetAdditionalKanjiCandidates(
                      lookupText);
        }

        var status = BuildStatusMessage(kind, vocabularyCard, kanjiCard);
        var dictionaryStatus = dictionaryManager.GetDictionaryStatusMessage();
        if (!string.IsNullOrWhiteSpace(dictionaryStatus))
        {
            var reloadStatus = dictionaryManager.ReloadStatus;
            var dictionaryStatusReplacesLookup
                      = reloadStatus.IsActive && !reloadStatus.HasActiveSnapshot
                        || containsJapanese
                           && !dictionaryManager.HasUsableLookupDictionaries;

            status = dictionaryStatusReplacesLookup
                           ? dictionaryStatus
                           : status + " " + dictionaryStatus;
        }

        var vocabularySummary = vocabularyCard is null ? "none"
                                : configuration.DiagnosticIncludeLookupText
                                          ? vocabularyCard.Expression
                                          : "found";
        var kanjiSummary = kanjiCard is null ? "none"
                           : configuration.DiagnosticIncludeLookupText
                                     ? kanjiCard.KanjiCharacter
                                     : "found";

        diagnostics.Information(
                  "Lookup",
                  $"Lookup result: kind={kind}; japanese={containsJapanese}; "
                            + $"vocabulary={vocabularySummary}; kanji={kanjiSummary}; "
                            + $"vocabulary candidates={vocabularyCandidateDetails.Count}; "
                            + $"additional kanji={additionalKanji.Count}.");

        return new LookupResult
        {
            RawText = rawText ?? string.Empty,
            CleanedText = cleaned,
            LookupText = lookupText,
            ContainsJapanese = containsJapanese,
            ContainsNonJapaneseLookupContent = hasNonJapaneseContent,
            TextKind = kind,
            StatusMessage = status,
            Source = source,
            VocabularyCard = vocabularyCard,
            KanjiCard = kanjiCard,
            VocabularyCandidates = vocabularyCandidates,
            VocabularyCandidateDetails = vocabularyCandidateDetails,
            AdditionalKanjiCandidates = additionalKanji
        };
    }

    /// <summary>
    /// Updates only the vocabulary/card side of the current result.
    /// </summary>
    public LookupResult FocusVocabularyCandidate(LookupResult existing,
                                                 string vocabularyCandidate)
    {
        if (string.IsNullOrWhiteSpace(vocabularyCandidate))
            return existing;

        using var timing = diagnostics.Measure(
                  "Lookup",
                  "Focus vocabulary candidate",
                  configuration.DiagnosticIncludeLookupText
                            ? $"candidate={vocabularyCandidate}"
                            : "candidate text omitted by privacy setting");

        var vocabularyCard = dictionaryManager.BuildVocabularyCard(
                  vocabularyCandidate, existing.CleanedText);
        if (vocabularyCard is null)
        {
            diagnostics.Warning("Lookup",
                                "A selected vocabulary candidate no longer "
                                + "produced a card result.");
            return existing;
        }

        return CopyResult(
                  existing,
                  source: "Vocabulary candidate click",
                  statusMessage: $"Focused vocabulary candidate: {vocabularyCard.Expression}",
                  vocabularyCard: vocabularyCard,
                  kanjiCard: existing.KanjiCard);
    }

    /// <summary>
    /// Updates only the kanji/card side of the current result.
    /// </summary>
    public LookupResult FocusKanjiCandidate(LookupResult existing,
                                            string kanjiCandidate)
    {
        if (string.IsNullOrWhiteSpace(kanjiCandidate))
            return existing;

        using var timing = diagnostics.Measure(
                  "Lookup",
                  "Focus kanji candidate",
                  configuration.DiagnosticIncludeLookupText
                            ? $"candidate={kanjiCandidate}"
                            : "candidate text omitted by privacy setting");

        var kanjiCard = dictionaryManager.BuildKanjiCard(kanjiCandidate,
                                                         existing.CleanedText);
        if (kanjiCard is null)
        {
            diagnostics.Warning("Lookup",
                                "A selected kanji candidate no longer produced "
                                + "a card result.");
            return existing;
        }

        return CopyResult(
                  existing,
                  source: "Kanji candidate click",
                  statusMessage: $"Focused kanji candidate: {kanjiCard.KanjiCharacter}",
                  vocabularyCard: existing.VocabularyCard,
                  kanjiCard: kanjiCard);
    }

    private static LookupResult CopyResult(LookupResult existing,
                                           string source,
                                           string statusMessage,
                                           VocabularyCardData? vocabularyCard,
                                           KanjiCardData? kanjiCard) => new()
                                           {
                                               RawText = existing.RawText,
                                               CleanedText = existing.CleanedText,
                                               LookupText = existing.LookupText,
                                               ContainsJapanese = existing.ContainsJapanese,
                                               ContainsNonJapaneseLookupContent
        = existing.ContainsNonJapaneseLookupContent,
                                               TextKind = existing.TextKind,
                                               StatusMessage = statusMessage,
                                               Source = source,
                                               VocabularyCard = vocabularyCard,
                                               KanjiCard = kanjiCard,
                                               VocabularyCandidates = existing.VocabularyCandidates,
                                               VocabularyCandidateDetails = existing.VocabularyCandidateDetails,
                                               AdditionalKanjiCandidates = existing.AdditionalKanjiCandidates
                                           };

    private static string BuildStatusMessage(JapaneseTextKind kind,
                                             VocabularyCardData? vocabularyCard,
                                             KanjiCardData? kanjiCard)
    {
        if (kind == JapaneseTextKind.Empty)
            return "No text was provided.";

        if (kind == JapaneseTextKind.NoJapanese)
            return "No Japanese characters were detected.";

        var hasVocab = vocabularyCard is not null;
        var hasKanji = kanjiCard is not null;

        return (hasVocab, hasKanji) switch
        {
            (true,
             true) => "Vocabulary and kanji dictionary results were found.",
            (true, false) => "Vocabulary result found. No kanji-card result "
                             + "was found.",
            (false,
             true) => "Kanji result found. No vocabulary result was found.",
            _ => "Japanese text detected, but no dictionary/card candidate was "
                 + "found yet. Try an exact word like 食べる, 日本, 誇る, or a single "
                 + "kanji."
        };
    }

    public void Dispose()
    {
        dictionaryManager.Dispose();
    }

}
