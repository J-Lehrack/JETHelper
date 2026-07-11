using System.Collections.Generic;
using System.Linq;
using JETHelper.Models;
using JETHelper.Utils;

namespace JETHelper.Services;

/// <summary>
/// LookupService is the main text-processing service.
///
/// It owns the lookup pipeline:
/// raw input -> cleaned text -> Japanese extraction -> dictionary/card-shaped data.
/// </summary>
public sealed class LookupService
{
    private readonly DictionaryManager dictionaryManager;

    public LookupService(Configuration configuration)
    {
        dictionaryManager = new DictionaryManager(configuration);
    }

    public void ReloadDictionaries()
        => dictionaryManager.ReloadDictionaries();

    public IReadOnlyList<DictionarySource> DictionarySources
        => dictionaryManager.DictionarySources;

    public LookupResult ProcessRawText(string? rawText,
                                       string source = "Unknown")
    {
        var cleaned = JapaneseText.CleanForLookup(rawText);
        var kind = JapaneseText.Classify(cleaned);
        var containsJapanese = JapaneseText.ContainsJapanese(cleaned);
        var hasNonJapaneseContent = JapaneseText.HasNonJapaneseLookupContent(cleaned);

        var lookupText = containsJapanese
                             ? JapaneseText.ExtractJapaneseForLookup(cleaned)
                             : string.Empty;

        VocabularyCardData? vocabularyCard = null;
        KanjiCardData? kanjiCard = null;
        var vocabularyCandidateDetails = new List<LookupCandidate>();
        var vocabularyCandidates = new List<string>();
        var additionalKanji = new List<string>();

        if (containsJapanese && !string.IsNullOrWhiteSpace(lookupText))
        {
            // Collect vocabulary candidates from the original lookup text first.
            // The default vocabulary card uses the first dictionary
            // match in reading order, while the candidate buttons let the user
            // switch focus without losing the copied sentence/context.
            vocabularyCandidateDetails = dictionaryManager.GetVocabularyCandidates(lookupText);
            vocabularyCandidates = vocabularyCandidateDetails.Select(c => c.Text).Distinct().ToList();
            var primaryVocabularyLookup = vocabularyCandidateDetails.FirstOrDefault()?.Text ?? lookupText;

            vocabularyCard = dictionaryManager.BuildVocabularyCard(primaryVocabularyLookup, cleaned);
            kanjiCard = dictionaryManager.BuildKanjiCard(lookupText, cleaned);
            additionalKanji = dictionaryManager.GetAdditionalKanjiCandidates(lookupText);
        }

        var status = BuildStatusMessage(kind, vocabularyCard, kanjiCard);
        var dictionaryStatus = dictionaryManager.GetDictionaryStatusMessage();
        if (!string.IsNullOrWhiteSpace(dictionaryStatus))
            status += " " + dictionaryStatus;

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
    ///
    /// This is used by clickable word candidate buttons. The important design
    /// point is that CleanedText and LookupText stay unchanged, so the original
    /// copied sentence remains attached to the card data.
    /// </summary>
    public LookupResult FocusVocabularyCandidate(LookupResult existing,
                                                 string vocabularyCandidate)
    {
        if (string.IsNullOrWhiteSpace(vocabularyCandidate))
            return existing;

        var vocabularyCard = dictionaryManager.BuildVocabularyCard(vocabularyCandidate, existing.CleanedText);
        if (vocabularyCard is null)
            return existing;

        return CopyResult(existing,
                          source: "Vocabulary candidate click",
                          statusMessage: $"Focused vocabulary candidate: {vocabularyCard.Expression}",
                          vocabularyCard: vocabularyCard,
                          kanjiCard: existing.KanjiCard);
    }

    /// <summary>
    /// Updates only the kanji/card side of the current result.
    ///
    /// This keeps the sentence and vocabulary preview intact while the user
    /// inspects individual kanji that appeared inside the copied text.
    /// </summary>
    public LookupResult FocusKanjiCandidate(LookupResult existing,
                                            string kanjiCandidate)
    {
        if (string.IsNullOrWhiteSpace(kanjiCandidate))
            return existing;

        var kanjiCard = dictionaryManager.BuildKanjiCard(kanjiCandidate, existing.CleanedText);
        if (kanjiCard is null)
            return existing;

        return CopyResult(existing,
                          source: "Kanji candidate click",
                          statusMessage: $"Focused kanji candidate: {kanjiCard.KanjiCharacter}",
                          vocabularyCard: existing.VocabularyCard,
                          kanjiCard: kanjiCard);
    }

    private static LookupResult CopyResult(LookupResult existing,
                                           string source,
                                           string statusMessage,
                                           VocabularyCardData? vocabularyCard,
                                           KanjiCardData? kanjiCard)
        => new()
        {
            RawText = existing.RawText,
            CleanedText = existing.CleanedText,
            LookupText = existing.LookupText,
            ContainsJapanese = existing.ContainsJapanese,
            ContainsNonJapaneseLookupContent = existing.ContainsNonJapaneseLookupContent,
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
            (true, true) => "Vocabulary and kanji dictionary results were found.",
            (true, false) => "Vocabulary result found. No kanji-card result was found.",
            (false, true) => "Kanji result found. No vocabulary result was found.",
            _ => "Japanese text detected, but no dictionary/card candidate was found yet. Try an exact word like 食べる, 日本, 誇る, or a single kanji."
        };
    }
}
