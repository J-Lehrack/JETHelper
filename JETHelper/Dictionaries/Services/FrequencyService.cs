using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Loads every inspected Yomitan term-frequency dictionary.
/// Each archive is isolated so a broken frequency list does not disable valid
/// definition or kanji dictionaries. The index is built by the background
/// reload worker before its snapshot is activated.
/// </summary>
public sealed class FrequencyService
{
    private readonly IReadOnlyList<DictionarySource> sources;
    private readonly Dictionary<string, FrequencyInfo> entries
        = new(StringComparer.Ordinal);
    private readonly List<string> sourceDictionaryNames = [];
    private readonly List<string> loadErrors = [];
    private bool loaded;

    public FrequencyService(DictionaryCatalog catalog)
    {
        sources = catalog.Select(DictionaryDataKind.TermFrequency);
    }

    public bool IsLoaded => loaded;
    public string? LoadError => loadErrors.Count == 0
        ? null
        : string.Join("; ", loadErrors);
    public IReadOnlyList<string> SourceDictionaryNames
        => sourceDictionaryNames;
    public int EntryCount => entries.Count;

    public void Preload(CancellationToken cancellationToken)
    {
        if (loaded)
            return;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                LoadFrequencyZip(source, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                loadErrors.Add($"{source.DisplayName}: {ex.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        loaded = true;
    }

    public FrequencyInfo Lookup(string lookupText)
    {
        if (!loaded
            || string.IsNullOrWhiteSpace(lookupText)
            || entries.Count == 0)
        {
            return new FrequencyInfo();
        }

        if (entries.TryGetValue(lookupText, out var exact))
            return exact;

        foreach (var candidate in GetLongestSubstringsFirst(
                     lookupText,
                     maxLength: 12))
        {
            if (entries.TryGetValue(candidate, out var found))
                return found;
        }

        return new FrequencyInfo();
    }

    private void LoadFrequencyZip(
        DictionarySource source,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(source.FilePath);
        var metaBanks = zip.Entries
            .Where(entry =>
                entry.Name.StartsWith(
                    "term_meta_bank_",
                    StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (metaBanks.Count == 0)
            return;

        var loadedReadableBank = false;

        foreach (var bank in metaBanks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (source.IsEntryUnreadable(bank.FullName))
            {
                loadErrors.Add(
                    $"{source.DisplayName}/{bank.Name} failed archive "
                    + "validation and was skipped.");
                continue;
            }

            try
            {
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
                    if ((rowIndex++ & 1023) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    TryAddFrequencyRow(row, source.DisplayName);
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
                    $"{source.DisplayName}/{bank.Name}: {ex.Message}");
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

    private void TryAddFrequencyRow(
        JsonElement row,
        string sourceName)
    {
        if (row.ValueKind != JsonValueKind.Array
            || row.GetArrayLength() < 3)
            return;

        var expression = row[0].GetString() ?? string.Empty;
        var type = row[1].GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expression)
            || !type.Equals("freq", StringComparison.OrdinalIgnoreCase))
            return;

        var rank = ExtractRank(row[2]);
        if (rank is null)
            return;

        var candidate = new FrequencyInfo
        {
            Rank = rank,
            Source = sourceName
        };

        // Lower ranks are more common, so keep the strongest available rank.
        if (!entries.TryGetValue(expression, out var existing)
            || existing.Rank is null
            || rank.Value < existing.Rank.Value)
        {
            entries[expression] = candidate;
        }
    }

    private static int? ExtractRank(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var number)
                => number,
            JsonValueKind.String when int.TryParse(
                element.GetString(), out var number)
                => number,
            JsonValueKind.Object => ExtractRankFromObject(element),
            _ => null
        };

    private static int? ExtractRankFromObject(JsonElement element)
    {
        foreach (var propertyName in new[]
                 {
                     "frequency", "rank", "value", "displayValue"
                 })
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            var rank = ExtractRank(property);
            if (rank is not null)
                return rank;
        }

        return null;
    }

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
