using System;
using System.Collections.Generic;
using JETHelper.Anki.Models;

namespace JETHelper.Lookup.Models;

/// <summary>
/// LookupResult is the full result of one lookup attempt.
///
/// It contains both early text-processing data, such as RawText and TextKind,
/// and later dictionary/card-shaped data, such as VocabularyCard and KanjiCard.
/// The UI can decide how much of this to show.
/// </summary>
public sealed class LookupResult
{
    public string RawText { get; init; } = string.Empty;
    public string CleanedText { get; init; } = string.Empty;
    public string LookupText { get; init; } = string.Empty;

    public bool ContainsJapanese { get; init; }
    public bool ContainsNonJapaneseLookupContent { get; init; }
    public JapaneseTextKind TextKind { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public string Source { get; init; } = "Unknown";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public VocabularyCardData? VocabularyCard { get; init; }
    public KanjiCardData? KanjiCard { get; init; }

    /// <summary>
    /// Vocabulary terms found inside the original lookup text.
    /// These are kept separately from LookupText so clicking one term can update
    /// the vocabulary preview without losing the original sentence/context.
    /// </summary>
    public List<string> VocabularyCandidates { get; init; } = [];

    /// <summary>
    /// Rich metadata for vocabulary candidate buttons. The older string list is
    /// still kept because it is convenient in simple UI code, but this richer
    /// list lets us separate likely terms from possible substring matches.
    /// </summary>
    public List<LookupCandidate> VocabularyCandidateDetails { get; init; } = [];

    /// <summary>
    /// Extra kanji found in the lookup text after the primary kanji candidate.
    /// Later these can become clickable buttons to switch the kanji preview.
    /// </summary>
    public List<string> AdditionalKanjiCandidates { get; init; } = [];

    public static LookupResult Empty(string message = "Enter or copy Japanese text to begin.") => new()
    {
        StatusMessage = message,
        TextKind = JapaneseTextKind.Empty,
        Source = "None"
    };
}
