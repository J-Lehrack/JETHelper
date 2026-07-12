using JETHelper.Dictionaries.Models;
using System.Collections.Generic;
using System.Linq;

namespace JETHelper.Anki.Models;

/// <summary>
/// Data shaped like the user's vocabulary Anki note.
///
/// The in-game UI will eventually show only a compact subset of this, but keeping
/// the full structure here lets dictionary parsing and Anki export evolve without
/// rewriting the lookup pipeline later.
/// </summary>
public sealed class VocabularyCardData
{
    public string Expression { get; init; } = string.Empty;
    public string Furigana { get; init; } = string.Empty;
    public List<DictionaryDefinition> EnglishDefinitions { get; init; } = [];
    public List<DictionaryDefinition> JapaneseDefinitions { get; init; } = [];
    public List<DictionaryDefinition> SlangDefinitions { get; init; } = [];
    public string Audio { get; init; } = string.Empty;
    public FrequencyInfo Frequency { get; init; } = new();
    public string Sentence { get; init; } = string.Empty;
    public PitchAccentInfo PitchAccent { get; init; } = new();
    public List<string> Tags { get; init; } = [];
    public List<string> EnglishDefinitionSources { get; init; } = [];
    public List<string> JapaneseDefinitionSources { get; init; } = [];
    public List<string> SlangDefinitionSources { get; init; } = [];

    public bool HasAnyDefinition => EnglishDefinitions.Count > 0
                                    || JapaneseDefinitions.Count > 0
                                    || SlangDefinitions.Count > 0;

    public string ReadingDisplay => string.IsNullOrWhiteSpace(Furigana) ? "—" : Furigana;

    public string FirstEnglishMeaning => EnglishDefinitions.FirstOrDefault()?.Meaning ?? "—";
}
