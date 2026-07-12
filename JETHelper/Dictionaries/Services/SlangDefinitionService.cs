using System.Collections.Generic;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Loads slang/media-oriented Yomitan term dictionaries.
///
/// Selection is metadata-driven, so KireiCake and other slang/media-oriented
/// dictionaries can contribute without filename-specific code.
/// </summary>
public sealed class SlangDefinitionService
{
    private readonly YomitanTermDictionaryService termService;

    public SlangDefinitionService(DictionaryCatalog catalog)
    {
        var sources = catalog.Select(
            DictionaryDataKind.TermDefinitions,
            language: null,
            role: DictionaryContentRole.SlangOrMedia);

        termService = new YomitanTermDictionaryService(
            "Slang and media definitions",
            sources);
    }

    public string? LoadError => termService.LoadError;
    public IReadOnlyList<string> SourceDictionaryNames
        => termService.SourceDictionaryNames;

    public List<DictionaryDefinition> Lookup(
        string lookupText,
        int maxResults = 4)
        => termService.Lookup(lookupText, maxResults);

    public List<LookupCandidate> FindVocabularyCandidates(
        string lookupText,
        DeinflectionService deinflector,
        int maxResults = 24)
        => termService.FindVocabularyCandidates(
            lookupText,
            deinflector,
            maxResults);
}
