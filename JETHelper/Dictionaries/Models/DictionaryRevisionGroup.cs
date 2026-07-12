using System.Collections.Generic;

namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Multiple revisions of the same dictionary title/data type. Different
/// revisions are intentionally retained as separate sources.
/// </summary>
public sealed class DictionaryRevisionGroup
{
    public string DisplayName { get; init; } = string.Empty;
    public List<DictionarySource> Sources { get; init; } = [];
}
