using System.Collections.Generic;
using System.Threading;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Loads general Japanese-language Yomitan term dictionaries.
/// Selection is based on inspected definition language rather than filenames.
/// </summary>
public sealed class JapaneseDefinitionService
{
    private readonly YomitanTermDictionaryService termService;

    public JapaneseDefinitionService(
        DictionaryCatalog catalog,
        bool collectMetrics)
    {
        var sources = catalog.Select(DictionaryDataKind.TermDefinitions,
                                     DictionaryLanguageKind.Japanese,
                                     DictionaryContentRole.General);

        termService = new YomitanTermDictionaryService(
            "Japanese definitions",
            sources,
            collectMetrics);
    }

    public string? LoadError => termService.LoadError;

    public void Preload(CancellationToken cancellationToken)
        => termService.Preload(cancellationToken);
    public bool IsLoaded => termService.IsLoaded;
    public int EntryCount => termService.EntryCount;
    public int StoredResultObjectCount
        => termService.StoredResultObjectCount;
    public IReadOnlyList<DictionaryLoadMetrics> LoadMetrics
        => termService.LoadMetrics;
    public IReadOnlyList<string>
              SourceDictionaryNames => termService.SourceDictionaryNames;

    public List<DictionaryDefinition>
    Lookup(string lookupText,
           int maxResults = 4) => termService.Lookup(lookupText, maxResults);

    public List<LookupCandidate> FindVocabularyCandidates(
              string lookupText,
              DeinflectionService deinflector,
              int maxResults
              = 24) => termService.FindVocabularyCandidates(lookupText,
                                                            deinflector,
                                                            maxResults);
}
