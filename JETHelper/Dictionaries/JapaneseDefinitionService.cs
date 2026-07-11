using System.Collections.Generic;
using JETHelper.Models;
using JETHelper.Services;

namespace JETHelper.Dictionaries;

/// <summary>
/// Loads general Japanese-language Yomitan term dictionaries.
/// Selection is based on inspected definition language rather than filenames.
/// </summary>
public sealed class JapaneseDefinitionService {
    private readonly YomitanTermDictionaryService termService;

    public JapaneseDefinitionService(DictionaryCatalog catalog)
    {
        var sources = catalog.Select(DictionaryDataKind.TermDefinitions,
                                     DictionaryLanguageKind.Japanese,
                                     DictionaryContentRole.General);

        termService = new YomitanTermDictionaryService("Japanese definitions",
                                                       sources);
    }

    public string? LoadError => termService.LoadError;
    public bool IsLoaded => termService.IsLoaded;
    public int EntryCount => termService.EntryCount;
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
