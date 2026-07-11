using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using JETHelper.Models;

namespace JETHelper.Dictionaries;

/// <summary>
/// Reads Yomitan dictionary metadata and bank names without loading the entire
/// dictionary into memory.
///
/// Classification is based primarily on the archive contents. Filename/title
/// hints are used only for semantic categories such as names or slang/media.
/// </summary>
public static class YomitanDictionaryInspector
{
    private const int DefinitionSamplesToInspect = 30;

    public static DictionarySource Inspect(
        string zipPath,
        DictionarySourceOrigin origin)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var indexEntry = zip.Entries.FirstOrDefault(
                entry => entry.Name.Equals("index.json",
                    StringComparison.OrdinalIgnoreCase));

            if (indexEntry is null)
            {
                return Invalid(zipPath, origin,
                    "The archive does not contain index.json.");
            }

            var index = ReadIndex(indexEntry, zipPath);
            var dataKinds = DetectDataKinds(zip);

            if (dataKinds == DictionaryDataKind.None)
            {
                return new DictionarySource
                {
                    Title = index.Title,
                    Revision = index.Revision,
                    Format = index.Format,
                    FilePath = zipPath,
                    Origin = origin,
                    DataKinds = dataKinds,
                    Language = DictionaryLanguageKind.Unknown,
                    ContentRole = DetectContentRole(index.Title),
                    Status = DictionaryInspectionStatus.Unsupported,
                    ErrorMessage = "No supported Yomitan term, kanji, or metadata banks were found."
                };
            }

            var role = DetectContentRole(index.Title);
            var language = dataKinds.HasFlag(DictionaryDataKind.TermDefinitions)
                ? DetectDefinitionLanguage(zip, index.Title, role)
                : DetectTitleLanguage(index.Title);

            var hasSupportedTermDefinitions
                = dataKinds.HasFlag(DictionaryDataKind.TermDefinitions)
                  && role != DictionaryContentRole.Names
                  && (role == DictionaryContentRole.SlangOrMedia
                      || language is DictionaryLanguageKind.English
                          or DictionaryLanguageKind.Japanese
                          or DictionaryLanguageKind.Mixed);
            var isCurrentlySupported
                = hasSupportedTermDefinitions
                  || dataKinds.HasFlag(DictionaryDataKind.KanjiDefinitions)
                  || dataKinds.HasFlag(DictionaryDataKind.TermFrequency);

            return new DictionarySource
            {
                Title = index.Title,
                Revision = index.Revision,
                Format = index.Format,
                FilePath = zipPath,
                Origin = origin,
                DataKinds = dataKinds,
                Language = language,
                ContentRole = role,
                Status = isCurrentlySupported
                    ? DictionaryInspectionStatus.Ready
                    : DictionaryInspectionStatus.Unsupported,
                ErrorMessage = isCurrentlySupported
                    ? string.Empty
                    : role == DictionaryContentRole.Names
                        ? "Name dictionaries are recognized but are not yet included in ordinary vocabulary lookup."
                        : "The dictionary data type or language was recognized but is not yet used by JETHelper."
            };
        }
        catch (InvalidDataException ex)
        {
            return Invalid(zipPath, origin, "Invalid ZIP archive: " + ex.Message);
        }
        catch (JsonException ex)
        {
            return Invalid(zipPath, origin, "Invalid dictionary JSON: " + ex.Message);
        }
        catch (Exception ex)
        {
            return Invalid(zipPath, origin, ex.Message);
        }
    }

    private static DictionarySource Invalid(
        string zipPath,
        DictionarySourceOrigin origin,
        string message)
        => new()
        {
            Title = Path.GetFileNameWithoutExtension(zipPath),
            FilePath = zipPath,
            Origin = origin,
            Status = DictionaryInspectionStatus.Invalid,
            ErrorMessage = message
        };

    private static DictionaryIndexMetadata ReadIndex(
        ZipArchiveEntry indexEntry,
        string zipPath)
    {
        using var stream = indexEntry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        return new DictionaryIndexMetadata(
            ReadValueAsString(root, "title",
                Path.GetFileNameWithoutExtension(zipPath)),
            ReadValueAsString(root, "revision", string.Empty),
            ReadValueAsString(root, "format", string.Empty));
    }

    private static string ReadValueAsString(
        JsonElement root,
        string propertyName,
        string fallback)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    private static DictionaryDataKind DetectDataKinds(ZipArchive zip)
    {
        var kinds = DictionaryDataKind.None;

        if (HasBanks(zip, "term_bank_"))
            kinds |= DictionaryDataKind.TermDefinitions;

        if (HasBanks(zip, "kanji_bank_"))
            kinds |= DictionaryDataKind.KanjiDefinitions;

        if (HasBanks(zip, "kanji_meta_bank_"))
            kinds |= DictionaryDataKind.OtherKanjiMetadata;

        var termMetaBanks = GetBanks(zip, "term_meta_bank_").ToList();
        if (termMetaBanks.Count > 0)
        {
            var metaTypes = ReadTermMetadataTypes(termMetaBanks);

            if (metaTypes.Contains("freq", StringComparer.OrdinalIgnoreCase))
                kinds |= DictionaryDataKind.TermFrequency;

            if (metaTypes.Contains("pitch", StringComparer.OrdinalIgnoreCase))
                kinds |= DictionaryDataKind.PitchAccent;

            if (metaTypes.Any(type =>
                    !type.Equals("freq", StringComparison.OrdinalIgnoreCase)
                    && !type.Equals("pitch", StringComparison.OrdinalIgnoreCase)))
            {
                kinds |= DictionaryDataKind.OtherTermMetadata;
            }

            // Even an empty or unfamiliar metadata bank is recognized as
            // metadata rather than being mistaken for a definition dictionary.
            if ((kinds & (DictionaryDataKind.TermFrequency
                          | DictionaryDataKind.PitchAccent
                          | DictionaryDataKind.OtherTermMetadata)) == 0)
            {
                kinds |= DictionaryDataKind.OtherTermMetadata;
            }
        }

        return kinds;
    }

    private static bool HasBanks(ZipArchive zip, string prefix)
        => zip.Entries.Any(entry =>
            entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ZipArchiveEntry> GetBanks(
        ZipArchive zip,
        string prefix)
        => zip.Entries.Where(entry =>
            entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> ReadTermMetadataTypes(
        IReadOnlyList<ZipArchiveEntry> banks)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bank in banks.Take(2))
        {
            try
            {
                using var stream = bank.Open();
                using var document = JsonDocument.Parse(stream);

                foreach (var row in document.RootElement.EnumerateArray().Take(50))
                {
                    if (row.ValueKind != JsonValueKind.Array
                        || row.GetArrayLength() < 2)
                        continue;

                    var type = row[1].ValueKind == JsonValueKind.String
                        ? row[1].GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(type))
                        types.Add(type);
                }
            }
            catch
            {
                // Full parsing is handled by the dedicated loader. Inspection
                // remains best-effort so one malformed metadata bank does not
                // prevent other dictionaries from being catalogued.
            }
        }

        return types;
    }

    private static DictionaryContentRole DetectContentRole(string title)
    {
        if (ContainsAny(title,
                "JMnedict", "proper name", "names", "人名", "固有名", "姓名"))
            return DictionaryContentRole.Names;

        if (ContainsAny(title,
                "Kirei", "slang", "anime", "manga", "俗語", "若者言葉"))
            return DictionaryContentRole.SlangOrMedia;

        return DictionaryContentRole.General;
    }

    private static DictionaryLanguageKind DetectDefinitionLanguage(
        ZipArchive zip,
        string title,
        DictionaryContentRole role)
    {
        var titleLanguage = DetectTitleLanguage(title);
        if (titleLanguage != DictionaryLanguageKind.Unknown)
            return titleLanguage;

        // Known English-oriented dictionaries may not advertise the language in
        // their title, so semantic role provides a safe hint.
        if (role is DictionaryContentRole.Names
            && title.Contains("JMnedict", StringComparison.OrdinalIgnoreCase))
            return DictionaryLanguageKind.English;

        var japaneseCharacters = 0;
        var latinCharacters = 0;
        var samplesSeen = 0;

        foreach (var bank in GetBanks(zip, "term_bank_").Take(2))
        {
            using var stream = bank.Open();
            using var document = JsonDocument.Parse(stream);

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array
                    || row.GetArrayLength() < 6)
                    continue;

                var text = ExtractGlossaryText(row[5]);
                CountLanguageCharacters(text,
                    ref japaneseCharacters,
                    ref latinCharacters);

                samplesSeen++;
                if (samplesSeen >= DefinitionSamplesToInspect)
                    break;
            }

            if (samplesSeen >= DefinitionSamplesToInspect)
                break;
        }

        if (japaneseCharacters == 0 && latinCharacters == 0)
            return DictionaryLanguageKind.Unknown;

        if (japaneseCharacters >= latinCharacters * 2)
            return DictionaryLanguageKind.Japanese;

        if (latinCharacters >= japaneseCharacters * 2)
            return DictionaryLanguageKind.English;

        return DictionaryLanguageKind.Mixed;
    }

    private static DictionaryLanguageKind DetectTitleLanguage(string title)
    {
        if (ContainsAny(title,
                "(English)", " English", "JMdict", "KANJIDIC", "KireiCake",
                "和英", "英和"))
            return DictionaryLanguageKind.English;

        if (ContainsAny(title,
                "国語", "大辞林", "大辞泉", "広辞苑", "明鏡", "新明解",
                "故事", "ことわざ"))
            return DictionaryLanguageKind.Japanese;

        return DictionaryLanguageKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term,
            StringComparison.OrdinalIgnoreCase));

    private static void CountLanguageCharacters(
        string text,
        ref int japaneseCharacters,
        ref int latinCharacters)
    {
        foreach (var character in text)
        {
            if (IsJapaneseCharacter(character))
                japaneseCharacters++;
            else if ((character >= 'A' && character <= 'Z')
                     || (character >= 'a' && character <= 'z'))
                latinCharacters++;
        }
    }

    private static bool IsJapaneseCharacter(char character)
        => character is >= '\u3040' and <= '\u30ff'
           || character is >= '\u3400' and <= '\u4dbf'
           || character is >= '\u4e00' and <= '\u9fff'
           || character is >= '\uf900' and <= '\ufaff';

    private static string ExtractGlossaryText(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(" ",
                element.EnumerateArray().Select(ExtractGlossaryText)),
            JsonValueKind.Object => string.Join(" ",
                element.EnumerateObject().Select(property =>
                    ExtractGlossaryText(property.Value))),
            _ => string.Empty
        };

    private readonly record struct DictionaryIndexMetadata(
        string Title,
        string Revision,
        string Format);
}
