namespace JETHelper.Dictionaries.Models;

/// <summary>
/// One definition line from one dictionary.
///
/// A single vocabulary word can have many DictionaryDefinition objects because
/// one dictionary entry may have several meanings, and later we may combine
/// multiple English, Japanese, slang/media, or user-supplied dictionaries.
/// </summary>
public sealed class DictionaryDefinition
{
    public string Expression { get; init; } = string.Empty;
    public string Reading { get; init; } = string.Empty;
    public string Meaning { get; init; } = string.Empty;
    public string PartOfSpeech { get; init; } = string.Empty;
    public string SourceDictionary { get; init; } = string.Empty;

    public string SourceDisplay => string.IsNullOrWhiteSpace(SourceDictionary) ? "Unknown dictionary" : SourceDictionary;
    public string PartOfSpeechDisplay => string.IsNullOrWhiteSpace(PartOfSpeech) ? string.Empty : PartOfSpeech;
}
