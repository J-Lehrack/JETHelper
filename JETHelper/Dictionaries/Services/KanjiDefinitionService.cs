using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using JETHelper.Anki.Models;
using JETHelper.Dictionaries.Catalog;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Dictionaries.Services;

/// <summary>
/// Loads every inspected Yomitan kanji dictionary and merges compatible data by
/// character. It is no longer tied to a specific kanjidic_english.zip filename.
/// </summary>
public sealed class KanjiDefinitionService
{
    private readonly IReadOnlyList<DictionarySource> sources;
    private readonly Dictionary<string, KanjiCardData> entries
        = new(StringComparer.Ordinal);
    private readonly List<string> sourceDictionaryNames = [];
    private readonly List<string> loadErrors = [];
    private bool loaded;

    public KanjiDefinitionService(DictionaryCatalog catalog)
    {
        sources = catalog.Select(DictionaryDataKind.KanjiDefinitions);
    }

    public bool IsLoaded => loaded;
    public string? LoadError => loadErrors.Count == 0
        ? null
        : string.Join("; ", loadErrors);
    public IReadOnlyList<string> SourceDictionaryNames
        => sourceDictionaryNames;

    public KanjiCardData? LookupFirstKanji(
        string lookupText,
        string sentence)
    {
        EnsureLoaded();

        foreach (var kanji in GetUniqueKanji(lookupText))
        {
            if (!entries.TryGetValue(kanji, out var entry))
                continue;

            return new KanjiCardData
            {
                KanjiCharacter = entry.KanjiCharacter,
                Meanings = entry.Meanings,
                Kunyomi = entry.Kunyomi,
                Onyomi = entry.Onyomi,
                Frequency = entry.Frequency,
                StrokeCount = entry.StrokeCount,
                Diagram = entry.Diagram,
                Sentence = sentence,
                SourceDictionaries = entry.SourceDictionaries,
                Tags = entry.Tags
            };
        }

        return null;
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;

        foreach (var source in sources)
        {
            try
            {
                LoadKanjiZip(source);
            }
            catch (Exception ex)
            {
                loadErrors.Add($"{source.DisplayName}: {ex.Message}");
            }
        }

        loaded = true;
    }

    private void LoadKanjiZip(DictionarySource source)
    {
        using var zip = ZipFile.OpenRead(source.FilePath);
        var banks = zip.Entries
            .Where(entry =>
                entry.Name.StartsWith(
                    "kanji_bank_",
                    StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (banks.Count == 0)
            return;

        var loadedReadableBank = false;

        foreach (var bank in banks)
        {
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

                if (document.RootElement.ValueKind
                    != JsonValueKind.Array)
                {
                    throw new JsonException(
                        $"{bank.Name} must contain a JSON array.");
                }

                foreach (var row in document.RootElement.EnumerateArray())
                {
                    var entry = ParseKanjiRow(
                        row,
                        source.DisplayName);
                    if (entry is null)
                        continue;

                    entries[entry.KanjiCharacter]
                        = entries.TryGetValue(
                            entry.KanjiCharacter,
                            out var existing)
                            ? Merge(existing, entry)
                            : entry;
                }

                loadedReadableBank = true;
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

    private static KanjiCardData? ParseKanjiRow(
        JsonElement row,
        string sourceName)
    {
        if (row.ValueKind != JsonValueKind.Array
            || row.GetArrayLength() < 6)
            return null;

        var character = row[0].GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(character))
            return null;

        var onyomi = SplitReadings(row[1].GetString());
        var kunyomi = SplitReadings(row[2].GetString());
        var tags = SplitReadings(row[3].GetString());
        var meanings = row[4].ValueKind == JsonValueKind.Array
            ? row[4].EnumerateArray()
                .Select(element => element.GetString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList()
            : [];

        int? frequency = null;
        int? strokes = null;

        if (row[5].ValueKind == JsonValueKind.Object)
        {
            frequency = TryReadIntegerProperty(row[5], "freq");
            strokes = TryReadIntegerProperty(row[5], "strokes");
        }

        tags.Insert(0, ToTag(sourceName));

        return new KanjiCardData
        {
            KanjiCharacter = character,
            Meanings = meanings,
            Kunyomi = kunyomi,
            Onyomi = onyomi,
            Frequency = new FrequencyInfo
            {
                Rank = frequency,
                Source = sourceName
            },
            StrokeCount = strokes,
            SourceDictionaries = [sourceName],
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static KanjiCardData Merge(
        KanjiCardData first,
        KanjiCardData second)
    {
        var frequency = SelectBestFrequency(first.Frequency, second.Frequency);

        return new KanjiCardData
        {
            KanjiCharacter = first.KanjiCharacter,
            Meanings = first.Meanings
                .Concat(second.Meanings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Kunyomi = first.Kunyomi
                .Concat(second.Kunyomi)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Onyomi = first.Onyomi
                .Concat(second.Onyomi)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Frequency = frequency,
            StrokeCount = first.StrokeCount ?? second.StrokeCount,
            Diagram = string.IsNullOrWhiteSpace(first.Diagram)
                ? second.Diagram
                : first.Diagram,
            SourceDictionaries = first.SourceDictionaries
                .Concat(second.SourceDictionaries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Tags = first.Tags
                .Concat(second.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static FrequencyInfo SelectBestFrequency(
        FrequencyInfo first,
        FrequencyInfo second)
    {
        if (!first.HasValue)
            return second;
        if (!second.HasValue)
            return first;

        return second.Rank!.Value < first.Rank!.Value ? second : first;
    }

    private static int? TryReadIntegerProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out var numericValue))
            return numericValue;

        return property.ValueKind == JsonValueKind.String
               && int.TryParse(property.GetString(), out var stringValue)
            ? stringValue
            : null;
    }

    private static List<string> SplitReadings(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value.Split(' ',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static IEnumerable<string> GetUniqueKanji(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is < 0x4E00 or > 0x9FFF)
                continue;

            var value = rune.ToString();
            if (seen.Add(value))
                yield return value;
        }
    }

    private static string ToTag(string sourceName)
    {
        var builder = new StringBuilder();
        foreach (var character in sourceName)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }
}
