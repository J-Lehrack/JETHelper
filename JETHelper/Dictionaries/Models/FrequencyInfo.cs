namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Frequency data for a word or kanji.
/// </summary>
public sealed class FrequencyInfo
{
    public int? Rank { get; init; }
    public string Source { get; init; } = string.Empty;

    public string DisplayText => Rank is null ? "—" : Rank.Value.ToString();
    public string SourceDisplay => string.IsNullOrWhiteSpace(Source) ? "—" : Source;
    public bool HasValue => Rank is not null;
}
