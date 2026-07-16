using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Loads general English-language Yomitan term dictionaries.
///
/// This service is intentionally source-agnostic: every inspected general English
/// term dictionary can contribute definitions and vocabulary candidates.
/// </summary>
public sealed class EnglishDefinitionService
{
    private readonly YomitanTermDictionaryService termService;

    public EnglishDefinitionService(DictionaryCatalog catalog)
    {
        var sources = catalog.Select(
                DictionaryDataKind.TermDefinitions,
                DictionaryLanguageKind.English,
                DictionaryContentRole.General)
            .Concat(catalog.Select(
                DictionaryDataKind.TermDefinitions,
                DictionaryLanguageKind.Mixed,
                DictionaryContentRole.General))
            .DistinctBy(source => source.FilePath,
                System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        termService = new YomitanTermDictionaryService(
            "English definitions",
            sources);
    }

    public bool IsLoaded => termService.IsLoaded;

    public void Preload(CancellationToken cancellationToken)
        => termService.Preload(cancellationToken);
    public string? LoadError => termService.LoadError;
    public IReadOnlyList<string> SourceDictionaryNames
        => termService.SourceDictionaryNames;
    public int EntryCount => termService.EntryCount;

    public List<DictionaryDefinition> Lookup(
        string lookupText,
        int maxResults = 8)
        => termService.Lookup(lookupText, maxResults);

    public bool HasExactEntry(string lookupText)
        => termService.HasExactEntry(lookupText);

    public List<DictionaryDefinition> LookupExact(
        string lookupText,
        int maxResults = 8)
        => termService.LookupExact(lookupText, maxResults);

    public List<LookupCandidate> FindVocabularyCandidates(
        string lookupText,
        DeinflectionService deinflector,
        int maxResults = 24)
        => termService.FindVocabularyCandidates(
            lookupText,
            deinflector,
            maxResults);
}
