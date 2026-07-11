using System;
using System.Collections.Generic;
using System.Linq;
using JETHelper.Models;

namespace JETHelper.Dictionaries;

/// <summary>
/// Discovers, inspects, classifies, and de-duplicates dictionary archives.
///
/// User-supplied dictionaries are preferred over bundled copies when both have
/// the same title, revision, and data type. Invalid and unsupported archives are
/// retained in the inventory so failures can be reported without stopping valid
/// dictionaries from loading.
/// </summary>
public sealed class DictionaryCatalog
{
    private readonly List<DictionarySource> sources;

    public DictionaryCatalog(string? configuredDictionaryFolderPath)
    {
        var inspected = DictionaryPathResolver
            .FindDictionaryZipCandidates(configuredDictionaryFolderPath)
            .Select(candidate => YomitanDictionaryInspector.Inspect(
                candidate.FilePath,
                candidate.Origin))
            .ToList();

        sources = DeDuplicate(inspected);
    }

    public IReadOnlyList<DictionarySource> Sources => sources;

    public IReadOnlyList<DictionarySource> ReadySources
        => sources.Where(source =>
                source.Status == DictionaryInspectionStatus.Ready)
            .ToList();

    public IReadOnlyList<DictionarySource> ProblemSources
        => sources.Where(source =>
                source.Status != DictionaryInspectionStatus.Ready)
            .ToList();

    public List<DictionarySource> Select(
        DictionaryDataKind requiredKind,
        DictionaryLanguageKind? language = null,
        DictionaryContentRole? role = null)
        => sources.Where(source =>
                source.Status == DictionaryInspectionStatus.Ready
                && source.HasKind(requiredKind)
                && (language is null || source.Language == language)
                && (role is null || source.ContentRole == role))
            .ToList();

    private static List<DictionarySource> DeDuplicate(
        IReadOnlyList<DictionarySource> inspected)
    {
        var results = new List<DictionarySource>();

        // Archives with readable metadata use logical identity. A user-configured
        // copy wins over the bundled copy because it may have been deliberately
        // installed or updated by the user.
        foreach (var group in inspected
                     .Where(source =>
                         source.Status != DictionaryInspectionStatus.Invalid)
                     .GroupBy(source => source.IdentityKey,
                         StringComparer.OrdinalIgnoreCase))
        {
            var preferred = group
                .OrderByDescending(source =>
                    source.Origin == DictionarySourceOrigin.UserConfigured)
                .ThenByDescending(source => source.FilePath,
                    StringComparer.OrdinalIgnoreCase)
                .First();

            results.Add(preferred);
        }

        // Invalid files do not have trustworthy metadata, so only exact path
        // duplicates are removed.
        results.AddRange(inspected
            .Where(source =>
                source.Status == DictionaryInspectionStatus.Invalid)
            .GroupBy(source => source.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()));

        return results
            .OrderBy(source => source.Status)
            .ThenBy(source => source.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
