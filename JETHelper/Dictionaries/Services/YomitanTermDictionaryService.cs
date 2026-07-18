using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Shared loader for supported Yomitan term dictionaries.
///
/// The caller supplies already-inspected dictionary sources. Each archive is
/// loaded independently so one malformed source cannot prevent the remaining
/// dictionaries from working. Index construction is performed explicitly by
/// the background reload worker before the containing snapshot is activated.
/// Lookup methods never perform synchronous first-use loading.
/// </summary>
public sealed class YomitanTermDictionaryService
{
    private readonly string serviceName;
    private readonly IReadOnlyList<DictionarySource> sources;
    private readonly bool collectMetrics;
    private readonly Dictionary<string, List<DictionaryDefinition>>
        entriesByExpression = new(StringComparer.Ordinal);
    private readonly List<string> sourceDictionaryNames = [];
    private readonly List<string> loadErrors = [];
    private readonly List<DictionaryLoadMetrics> loadMetrics = [];
    private int storedResultObjectCount;
    private bool loaded;

    public YomitanTermDictionaryService(
        string serviceName,
        IReadOnlyList<DictionarySource> sources,
        bool collectMetrics)
    {
        this.serviceName = serviceName;
        this.sources = sources;
        this.collectMetrics = collectMetrics;
    }

    public bool IsLoaded => loaded;
    public string? LoadError => loadErrors.Count == 0
        ? null
        : string.Join("; ", loadErrors);
    public IReadOnlyList<string> SourceDictionaryNames
        => sourceDictionaryNames;
    public int EntryCount => entriesByExpression.Count;
    public int StoredResultObjectCount => storedResultObjectCount;
    public IReadOnlyList<DictionaryLoadMetrics> LoadMetrics => loadMetrics;

    /// <summary>
    /// Parses all compatible term banks and builds the lookup index.
    ///
    /// The service belongs to a not-yet-published replacement snapshot, so this
    /// method is called exactly once by the serialized background reload worker.
    /// Cancellation discards the entire replacement snapshot.
    /// </summary>
    public void Preload(CancellationToken cancellationToken)
    {
        if (loaded)
            return;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = collectMetrics ? Stopwatch.StartNew() : null;
            var keysBefore = entriesByExpression.Count;
            var resultsBefore = storedResultObjectCount;
            var errorsBefore = loadErrors.Count;
            var banksDiscovered = 0;
            var banksProcessed = 0;
            var banksSkipped = 0;
            long rowsProcessed = 0;

            try
            {
                LoadTermZip(
                    source,
                    cancellationToken,
                    ref banksDiscovered,
                    ref banksProcessed,
                    ref banksSkipped,
                    ref rowsProcessed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loadErrors.Add(
                    $"{serviceName}: {source.DisplayName}: {ex.Message}");
            }
            finally
            {
                if (collectMetrics)
                {
                    stopwatch!.Stop();
                    loadMetrics.Add(new DictionaryLoadMetrics
                    {
                        ServiceName = serviceName,
                        SourceName = source.DisplayName,
                        SourcePath = source.FilePath,
                        BanksDiscovered = banksDiscovered,
                        BanksProcessed = banksProcessed,
                        BanksSkipped = banksSkipped,
                        RowsProcessed = rowsProcessed,
                        LookupKeysAdded
                            = entriesByExpression.Count - keysBefore,
                        StoredResultObjectsAdded
                            = storedResultObjectCount - resultsBefore,
                        ErrorCount = loadErrors.Count - errorsBefore,
                        DurationMilliseconds
                            = stopwatch.Elapsed.TotalMilliseconds
                    });
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        loaded = true;
    }

    public List<DictionaryDefinition> Lookup(
        string lookupText,
        int maxResults = 8)
    {
        if (!loaded
            || string.IsNullOrWhiteSpace(lookupText)
            || entriesByExpression.Count == 0)
        {
            return [];
        }

        if (entriesByExpression.TryGetValue(lookupText, out var exact))
            return exact.Take(maxResults).ToList();

        foreach (var candidate in GetLongestSubstringsFirst(
                     lookupText,
                     maxLength: 12))
        {
            if (entriesByExpression.TryGetValue(candidate, out var found))
                return found.Take(maxResults).ToList();
        }

        return [];
    }

    public bool HasExactEntry(string lookupText)
        => loaded
           && !string.IsNullOrWhiteSpace(lookupText)
           && entriesByExpression.TryGetValue(lookupText, out var found)
           && found.Count > 0;

    public List<DictionaryDefinition> LookupExact(
        string lookupText,
        int maxResults = 8)
    {
        if (!loaded || string.IsNullOrWhiteSpace(lookupText))
            return [];

        return entriesByExpression.TryGetValue(lookupText, out var exact)
            ? exact.Take(maxResults).ToList()
            : [];
    }

    /// <summary>
    /// Finds terms that appear inside a longer sentence. This remains a
    /// dictionary-substring approach rather than a full tokenizer, but it works
    /// with every term dictionary loaded by this service.
    /// </summary>
    public List<LookupCandidate> FindVocabularyCandidates(
        string lookupText,
        DeinflectionService deinflector,
        int maxResults = 24)
    {
        if (!loaded
            || string.IsNullOrWhiteSpace(lookupText)
            || entriesByExpression.Count == 0)
        {
            return [];
        }

        var matches = new List<LookupCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var maxLength = Math.Min(14, lookupText.Length);

        void AddMatch(
            string text,
            string surfaceText,
            int start,
            int surfaceLength,
            bool isDeinflected,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(text) || !HasExactEntry(text))
                return;

            var key = $"{text}|{surfaceText}|{start}|{isDeinflected}";
            if (!seen.Add(key))
                return;

            matches.Add(new LookupCandidate
            {
                Text = text,
                SurfaceText = surfaceText,
                StartIndex = start,
                SurfaceLength = surfaceLength,
                IsDeinflected = isDeinflected,
                Reason = reason,
                IsLikely = isDeinflected || surfaceLength >= 2
            });
        }

        for (var start = 0; start < lookupText.Length; start++)
        {
            for (var length = Math.Min(maxLength,
                     lookupText.Length - start);
                 length >= 1;
                 length--)
            {
                var surface = lookupText.Substring(start, length);

                if (HasExactEntry(surface))
                {
                    AddMatch(surface,
                        surface,
                        start,
                        length,
                        false,
                        "exact dictionary match");
                }

                if (length < 2)
                    continue;

                foreach (var candidate in deinflector.Generate(surface))
                {
                    AddMatch(candidate.Text,
                        surface,
                        start,
                        length,
                        true,
                        candidate.Reason);
                }
            }
        }

        return matches
            .OrderBy(match => match.StartIndex)
            .ThenByDescending(match => match.SurfaceLength)
            .ThenBy(match => match.IsDeinflected ? 0 : 1)
            .ThenBy(match => match.Text, StringComparer.Ordinal)
            .GroupBy(match => match.Text, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(maxResults)
            .ToList();
    }

    private void LoadTermZip(
        DictionarySource source,
        CancellationToken cancellationToken,
        ref int banksDiscovered,
        ref int banksProcessed,
        ref int banksSkipped,
        ref long rowsProcessed)
    {
        using var zip = ZipFile.OpenRead(source.FilePath);
        var banks = zip.Entries
            .Where(entry =>
                entry.Name.StartsWith(
                    "term_bank_",
                    StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (collectMetrics)
            banksDiscovered = banks.Count;

        if (banks.Count == 0)
            return;

        var loadedReadableBank = false;

        foreach (var bank in banks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (source.IsEntryUnreadable(bank.FullName))
            {
                loadErrors.Add(
                    $"{serviceName}: {source.DisplayName}/{bank.Name} "
                    + "failed archive validation and was skipped.");
                if (collectMetrics)
                    banksSkipped++;
                continue;
            }

            try
            {
                if (collectMetrics)
                    banksProcessed++;
                using var stream = bank.Open();
                using var document = JsonDocument.Parse(stream);
                cancellationToken.ThrowIfCancellationRequested();

                if (document.RootElement.ValueKind
                    != JsonValueKind.Array)
                {
                    throw new JsonException(
                        $"{bank.Name} must contain a JSON array.");
                }

                var rowIndex = 0;
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    if (collectMetrics)
                        rowsProcessed++;
                    if ((rowIndex++ & 1023) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    var definition = ParseTermRow(
                        row,
                        source.DisplayName);
                    if (definition is null
                        || string.IsNullOrWhiteSpace(
                            definition.Expression))
                    {
                        continue;
                    }

                    var retained = Add(
                        definition.Expression,
                        definition);

                    if (!string.IsNullOrWhiteSpace(definition.Reading)
                        && definition.Reading
                        != definition.Expression)
                    {
                        retained |= Add(definition.Reading, definition);
                    }

                    if (retained && collectMetrics)
                        storedResultObjectCount++;
                }

                loadedReadableBank = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loadErrors.Add(
                    $"{serviceName}: {source.DisplayName}/{bank.Name}: "
                    + ex.Message);
            }
        }

        if (loadedReadableBank
            && !sourceDictionaryNames.Contains(
                source.DisplayName,
                StringComparer.OrdinalIgnoreCase))
        {
            sourceDictionaryNames.Add(source.DisplayName);
        }
    }

    private bool Add(string key, DictionaryDefinition definition)
    {
        if (!entriesByExpression.TryGetValue(key, out var list))
        {
            list = [];
            entriesByExpression[key] = list;
        }

        if (list.Any(existing =>
                existing.Expression == definition.Expression
                && existing.Reading == definition.Reading
                && existing.Meaning == definition.Meaning
                && existing.SourceDictionary == definition.SourceDictionary))
        {
            return false;
        }

        list.Add(definition);
        return true;
    }

    private static DictionaryDefinition? ParseTermRow(
        JsonElement row,
        string sourceDictionary)
    {
        if (row.ValueKind != JsonValueKind.Array
            || row.GetArrayLength() < 6)
            return null;

        var expression = row[0].GetString() ?? string.Empty;
        var reading = row[1].GetString() ?? string.Empty;
        var tags = row[2].GetString() ?? string.Empty;
        var glossary = ExtractGlossaryText(row[5]);

        if (string.IsNullOrWhiteSpace(glossary))
            return null;

        return new DictionaryDefinition
        {
            Expression = expression,
            Reading = reading,
            PartOfSpeech = tags,
            Meaning = glossary.Trim(),
            SourceDictionary = sourceDictionary
        };
    }

    private static string ExtractGlossaryText(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join("; ",
                element.EnumerateArray()
                    .Select(ExtractGlossaryText)
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            JsonValueKind.Object => string.Join("; ",
                element.EnumerateObject()
                    .Select(property => ExtractGlossaryText(property.Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            _ => string.Empty
        };

    private static IEnumerable<string> GetLongestSubstringsFirst(
        string text,
        int maxLength)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cappedMax = Math.Min(maxLength, text.Length);

        for (var length = cappedMax; length >= 1; length--)
        {
            for (var start = 0; start <= text.Length - length; start++)
            {
                var candidate = text.Substring(start, length);
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }
}
