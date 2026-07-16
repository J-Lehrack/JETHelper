using System;
using System.Collections.Generic;
using System.Linq;
using JETHelper.Dictionaries.Inspection;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Dictionaries.Catalog;

/// <summary>
/// Discovers, inspects, classifies, and de-duplicates dictionary archives.
///
/// User-supplied dictionaries are preferred over bundled copies when both have
/// the same logical identity and equivalent health. A fully readable copy is
/// preferred over a partial copy so a damaged user archive cannot hide a clean
/// bundled source. Invalid and unsupported archives remain in the inventory so
/// failures can be reported without stopping valid dictionaries from loading.
/// </summary>
public sealed class DictionaryCatalog {
    private readonly List<DictionarySource> sources;
    private readonly List<DictionaryDuplicateDecision> duplicateDecisions;
    private readonly List<DictionaryRevisionGroup> revisionGroups;

    public DictionaryCatalog(string? configuredDictionaryFolderPath) :
          this(DictionaryPathResolver
                         .FindDictionaryZipCandidates(
                                   configuredDictionaryFolderPath)
                         .Select(candidate => YomitanDictionaryInspector.Inspect(
                                           candidate.FilePath,
                                           candidate.Origin))
                         .ToList())
    {
    }

    private DictionaryCatalog(IReadOnlyList<DictionarySource> inspected)
    {
        sources = DeDuplicate(inspected, out duplicateDecisions);
        revisionGroups = FindMultipleRevisionGroups(sources);
    }

    /// <summary>
    /// Builds a catalog from sources that were already inspected by a
    /// background reload operation.
    /// </summary>
    public static DictionaryCatalog FromInspectedSources(
              IReadOnlyList<DictionarySource> inspected) => new(inspected);

    public IReadOnlyList<DictionarySource> Sources => sources;

    public IReadOnlyList<DictionaryDuplicateDecision>
              DuplicateDecisions => duplicateDecisions;

    public IReadOnlyList<DictionaryRevisionGroup>
              RevisionGroups => revisionGroups;

    /// <summary>
    /// Sources that can contribute data. This includes partially readable
    /// sources whose valid banks remain usable.
    /// </summary>
    public IReadOnlyList<DictionarySource>
              ReadySources => sources.Where(source => source.IsUsable).ToList();

    public IReadOnlyList<DictionarySource>
              WarningSources => sources.Where(source => source.HasWarnings)
                                          .ToList();

    public IReadOnlyList<DictionarySource>
              ProblemSources => sources.Where(source => !source.IsUsable)
                                          .ToList();

    public List<DictionarySource>
    Select(DictionaryDataKind requiredKind,
           DictionaryLanguageKind? language = null,
           DictionaryContentRole? role
           = null) => sources
                                .Where(source => source.IsUsable
                                                 && source.HasKind(requiredKind)
                                                 && (language is null
                                                     || source.Language
                                                                  == language)
                                                 && (role is null
                                                     || source.ContentRole
                                                                  == role))
                                .ToList();

    private static List<DictionarySource>
    DeDuplicate(IReadOnlyList<DictionarySource> inspected,
                out List<DictionaryDuplicateDecision> decisions)
    {
        var results = new List<DictionarySource>();
        decisions = [];

        foreach (var group in inspected
                           .Where(source => source.Status
                                            != DictionaryInspectionStatus
                                                         .Invalid)
                           .GroupBy(source => source.IdentityKey,
                  StringComparer.OrdinalIgnoreCase)) {
            var ordered
                      = group.OrderByDescending(GetHealthPriority)
                                  .ThenByDescending(
                                            source => source.Origin
                                                      == DictionarySourceOrigin
                                                                   .UserConfigured)
                                  .ThenByDescending(source => GetFileLength(
                                                              source.FilePath))
                                  .ThenByDescending(
                                            source => GetLastWriteTimeUtc(
                                                      source.FilePath))
                                  .ThenByDescending(
                                            source => source.FilePath,
                                            StringComparer.OrdinalIgnoreCase)
                                  .ToList();

            var preferred = ordered[0];
            results.Add(preferred);

            if (ordered.Count <= 1)
                continue;

            var ignored = ordered.Skip(1).ToList();
            decisions.Add(new DictionaryDuplicateDecision {
                Preferred = preferred,
                Ignored = ignored,
                Reason = BuildDuplicateReason(preferred, ignored)
            });
        }

        // Invalid files do not have trustworthy metadata, so only exact path
        // duplicates are removed.
        results.AddRange(inspected.Where(source => source.Status
                                                   == DictionaryInspectionStatus
                                                                .Invalid)
                                   .GroupBy(source => source.FilePath,
                                            StringComparer.OrdinalIgnoreCase)
                                   .Select(group => group.First()));

        return results.OrderBy(source => source.Status)
                  .ThenBy(source => source.DisplayName,
                          StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static string
    BuildDuplicateReason(DictionarySource preferred,
                         IReadOnlyList<DictionarySource> ignored)
    {
        var preferredHealth = GetHealthPriority(preferred);
        var ignoredBestHealth = ignored.Max(GetHealthPriority);

        if (preferredHealth > ignoredBestHealth) {
            return "JETHelper selected the more completely readable copy. "
                   + "A damaged or partially readable duplicate does not "
                   + "override a healthier source.";
        }

        var userCopyPreferred
                  = preferred.Origin == DictionarySourceOrigin.UserConfigured
                    && ignored.Any(source => source.Origin
                                             == DictionarySourceOrigin.Bundled);

        if (userCopyPreferred) {
            return "A matching user-configured copy takes priority over the "
                   + "bundled copy when both have equivalent readability.";
        }

        return "The same dictionary identity was found more than once in the "
               + "same source type. JETHelper selected the largest archive as "
               + "the most likely complete copy, then used modification time "
               + "and path as deterministic tie-breakers.";
    }

    private static int
    GetHealthPriority(DictionarySource source) => source.Status switch {
        DictionaryInspectionStatus.Ready => 3,
        DictionaryInspectionStatus.ReadyWithWarnings => 2,
        DictionaryInspectionStatus.Unsupported => 1,
        _ => 0
    };

    private static long GetFileLength(string path)
    {
        try {
            return new System.IO.FileInfo(path).Length;
        }
        catch {
            return -1;
        }
    }

    private static DateTime GetLastWriteTimeUtc(string path)
    {
        try {
            return System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch {
            return DateTime.MinValue;
        }
    }

    private static List<DictionaryRevisionGroup> FindMultipleRevisionGroups(
              IReadOnlyList<DictionarySource> deDuplicatedSources)
    {
        return deDuplicatedSources
                  .Where(source => source.Status
                                   != DictionaryInspectionStatus.Invalid)
                  .GroupBy(source => string.Join(
                                     "|",
                                     source.DisplayName.Trim()
                                               .ToUpperInvariant(),
                                     ((int) source.DataKinds).ToString()),
                           StringComparer.OrdinalIgnoreCase)
                  .Where(group => group.Select(source => source.Revision)
                                            .Distinct(StringComparer
                                                                .OrdinalIgnoreCase)
                                            .Count()
                                  > 1)
                  .Select(group => new DictionaryRevisionGroup {
                      DisplayName = group.First().DisplayName,
                      Sources
                      = group.OrderByDescending(
                                       source => source.Origin
                                                 == DictionarySourceOrigin
                                                              .UserConfigured)
                                  .ThenByDescending(
                                            source => source.Revision,
                                            StringComparer.OrdinalIgnoreCase)
                                  .ToList()
                  })
                  .OrderBy(group => group.DisplayName,
                           StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }
}
