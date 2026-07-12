using System.Collections.Generic;

namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Describes how the dictionary catalog handled multiple copies with the same
/// logical identity (title, revision, and data kinds).
/// </summary>
public sealed class DictionaryDuplicateDecision
{
    public DictionarySource Preferred { get; init; } = new();
    public List<DictionarySource> Ignored { get; init; } = [];
    public string Reason { get; init; } = string.Empty;
}
