using System.Collections.Generic;
using System.Linq;

namespace JETHelper.Models;

/// <summary>
/// Data shaped like the user's kanji Anki note.
/// </summary>
public sealed class KanjiCardData
{
    public string KanjiCharacter { get; init; } = string.Empty;
    public List<string> Meanings { get; init; } = [];
    public List<string> Kunyomi { get; init; } = [];
    public List<string> Onyomi { get; init; } = [];
    public FrequencyInfo Frequency { get; init; } = new();
    public string Sentence { get; init; } = string.Empty;
    public int? StrokeCount { get; init; }
    public string Diagram { get; init; } = string.Empty;
    public List<string> SourceDictionaries { get; init; } = [];
    public List<string> Tags { get; init; } = [];

    public string MeaningsDisplay => Meanings.Count == 0 ? "—" : string.Join("; ", Meanings);
    public string KunyomiDisplay => Kunyomi.Count == 0 ? "—" : string.Join("、", Kunyomi);
    public string OnyomiDisplay => Onyomi.Count == 0 ? "—" : string.Join("、", Onyomi);
    public string StrokeDisplay => StrokeCount is null ? "—" : StrokeCount.Value.ToString();
}
