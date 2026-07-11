using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using JETHelper.Models;
using JETHelper.Services;

namespace JETHelper.Dictionaries;

/// <summary>
/// Shared loader for supported Yomitan term dictionaries.
///
/// The caller supplies already-inspected dictionary sources. Each archive is
/// loaded independently so one malformed source cannot prevent the remaining
/// dictionaries from working.
/// </summary>
public sealed class YomitanTermDictionaryService
{
    private readonly string serviceName;
    private readonly IReadOnlyList<DictionarySource> sources;
    private readonly Dictionary<string, List<DictionaryDefinition>>
        entriesByExpression = new(StringComparer.Ordinal);
    private readonly List<string> sourceDictionaryNames = [];
    private readonly List<string> loadErrors = [];
    private bool loaded;

    public YomitanTermDictionaryService(
        string serviceName,
        IReadOnlyList<DictionarySource> sources)
    {
        this.serviceName = serviceName;
        this.sources = sources;
    }

    public bool IsLoaded => loaded;
    public string? LoadError => loadErrors.Count == 0
        ? null
        : string.Join("; ", loadErrors);
    public IReadOnlyList<string> SourceDictionaryNames
        => sourceDictionaryNames;
    public int EntryCount => entriesByExpression.Count;

    public List<DictionaryDefinition> Lookup(
        string lookupText,
        int maxResults = 8)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(lookupText)
            || entriesByExpression.Count == 0)
            return [];

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
    {
        EnsureLoaded();
        return !string.IsNullOrWhiteSpace(lookupText)
               && entriesByExpression.TryGetValue(lookupText, out var found)
               && found.Count > 0;
    }

    public List<DictionaryDefinition> LookupExact(
        string lookupText,
        int maxResults = 8)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(lookupText))
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
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(lookupText)
            || entriesByExpression.Count == 0)
            return [];

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

    private void EnsureLoaded()
    {
        if (loaded)
            return;

        foreach (var source in sources)
        {
            try
            {
                LoadTermZip(source);
            }
            catch (Exception ex)
            {
                loadErrors.Add($"{source.DisplayName}: {ex.Message}");
            }
        }

        loaded = true;
    }

    private void LoadTermZip(DictionarySource source)
    {
        using var zip = ZipFile.OpenRead(source.FilePath);
        var banks = zip.Entries
            .Where(entry =>
                entry.Name.StartsWith("term_bank_",
                    StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".json",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (banks.Count == 0)
            return;

        if (!sourceDictionaryNames.Contains(source.DisplayName,
                StringComparer.OrdinalIgnoreCase))
        {
            sourceDictionaryNames.Add(source.DisplayName);
        }

        foreach (var bank in banks)
        {
            using var stream = bank.Open();
            using var document = JsonDocument.Parse(stream);

            foreach (var row in document.RootElement.EnumerateArray())
            {
                var definition = ParseTermRow(row, source.DisplayName);
                if (definition is null
                    || string.IsNullOrWhiteSpace(definition.Expression))
                    continue;

                Add(definition.Expression, definition);

                if (!string.IsNullOrWhiteSpace(definition.Reading)
                    && definition.Reading != definition.Expression)
                {
                    Add(definition.Reading, definition);
                }
            }
        }
    }

    private void Add(string key, DictionaryDefinition definition)
    {
        if (!entriesByExpression.TryGetValue(key, out var list))
        {
            list = [];
            entriesByExpression[key] = list;
        }

        if (!list.Any(existing =>
                existing.Expression == definition.Expression
                && existing.Reading == definition.Reading
                && existing.Meaning == definition.Meaning
                && existing.SourceDictionary == definition.SourceDictionary))
        {
            list.Add(definition);
        }
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
