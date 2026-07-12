namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Semantic purpose of a term dictionary. This keeps name dictionaries and
/// slang/media dictionaries from being mixed into ordinary vocabulary results.
/// </summary>
public enum DictionaryContentRole { General, Names, SlangOrMedia, Unknown }
