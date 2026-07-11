using System;

namespace JETHelper.Models;

/// <summary>
/// Describes the kinds of data discovered inside a supported dictionary ZIP.
/// A single archive may contain more than one kind of bank.
/// </summary>
[Flags]
public enum DictionaryDataKind
{
    None = 0,
    TermDefinitions = 1 << 0,
    KanjiDefinitions = 1 << 1,
    TermFrequency = 1 << 2,
    PitchAccent = 1 << 3,
    OtherTermMetadata = 1 << 4,
    OtherKanjiMetadata = 1 << 5
}
