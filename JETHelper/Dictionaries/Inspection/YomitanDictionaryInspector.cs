using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Dictionaries.Inspection;

/// <summary>
/// Reads Yomitan dictionary metadata and validates archive entries before lookup
/// services load them.
///
/// Classification is based primarily on archive contents. Filename/title hints
/// are used only for semantic categories such as names or slang/media.
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
                entry => entry.Name.Equals(
                    "index.json",
                    StringComparison.OrdinalIgnoreCase));

            if (indexEntry is null)
            {
                return Invalid(
                    zipPath,
                    origin,
                    "No Yomitan index.json file was found. The archive may be incomplete, corrupted, or not a Yomitan dictionary. Download it again or remove it from the dictionary folder.",
                    "The archive does not contain index.json.");
            }

            var index = ReadIndex(indexEntry, zipPath);
            var validation = YomitanArchiveValidator.Validate(zip);
            var unreadableEntries = new HashSet<string>(
                validation.Problems.Select(problem => problem.EntryName),
                StringComparer.OrdinalIgnoreCase);

            var dataKinds = DetectDataKinds(zip, unreadableEntries);
            var hasRecognizedBankNames = HasRecognizedBankNames(zip);

            if (dataKinds == DictionaryDataKind.None)
            {
                if (hasRecognizedBankNames && validation.Problems.Count > 0)
                {
                    return Invalid(
                        zipPath,
                        origin,
                        "JETHelper found dictionary bank files, but none of the supported banks could be read. The archive may be corrupted, malformed, incomplete, or incompatible. Download a fresh copy or remove it from the dictionary folder.",
                        BuildTechnicalDetails(
                            validation,
                            "No readable supported dictionary banks remained."),
                        validation.Problems.Select(problem => problem.EntryName));
                }

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
                    ErrorMessage = validation.Problems.Count > 0
                        ? BuildPartialReadMessage(
                            validation.Problems.Count,
                            currentlySupported: false)
                        : "No supported Yomitan dictionary banks were found. The archive may be incomplete or contain a data type JETHelper does not support yet.",
                    TechnicalDetails = validation.Problems.Count > 0
                        ? BuildTechnicalDetails(
                            validation,
                            "No readable term_bank_, kanji_bank_, or supported term_meta_bank_ data was detected.")
                        : "No term_bank_, kanji_bank_, term_meta_bank_, or kanji_meta_bank_ JSON files were detected.",
                    UnreadableEntries = validation.Problems
                        .Select(problem => problem.EntryName)
                        .ToList()
                };
            }

            var role = DetectContentRole(index.Title);
            var language = dataKinds.HasFlag(DictionaryDataKind.TermDefinitions)
                ? DetectDefinitionLanguage(
                    zip,
                    index.Title,
                    role,
                    unreadableEntries)
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

            var status = isCurrentlySupported
                ? validation.Problems.Count > 0
                    ? DictionaryInspectionStatus.ReadyWithWarnings
                    : DictionaryInspectionStatus.Ready
                : DictionaryInspectionStatus.Unsupported;

            var userMessage = status switch
            {
                DictionaryInspectionStatus.ReadyWithWarnings
                    => BuildPartialReadMessage(
                        validation.Problems.Count,
                        currentlySupported: true),
                DictionaryInspectionStatus.Unsupported
                    when validation.Problems.Count > 0
                    => BuildPartialReadMessage(
                        validation.Problems.Count,
                        currentlySupported: false),
                DictionaryInspectionStatus.Unsupported
                    when role == DictionaryContentRole.Names
                    => "Name dictionaries are recognized but are not yet included in ordinary vocabulary lookup.",
                DictionaryInspectionStatus.Unsupported
                    => "This Yomitan dictionary was recognized, but its data type or language is not currently used by JETHelper.",
                _ => string.Empty
            };

            var technicalDetails = validation.Problems.Count > 0
                ? BuildTechnicalDetails(
                    validation,
                    $"Detected data kinds: {dataKinds}; language: {language}; content role: {role}.")
                : isCurrentlySupported
                    ? string.Empty
                    : $"Detected data kinds: {dataKinds}; language: {language}; content role: {role}.";

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
                Status = status,
                ErrorMessage = userMessage,
                TechnicalDetails = technicalDetails,
                UnreadableEntries = validation.Problems
                    .Select(problem => problem.EntryName)
                    .ToList()
            };
        }
        catch (InvalidDataException ex)
        {
            return Invalid(
                zipPath,
                origin,
                "The archive could not be opened as a valid ZIP file. It may be corrupted or incomplete. Download it again or remove it from the dictionary folder.",
                ex.ToString());
        }
        catch (JsonException ex)
        {
            return Invalid(
                zipPath,
                origin,
                "Dictionary metadata could not be read. The archive may be corrupted, malformed, incomplete, or incompatible. Download a fresh copy or remove it from the dictionary folder.",
                ex.ToString());
        }
        catch (Exception ex)
        {
            return Invalid(
                zipPath,
                origin,
                "JETHelper could not inspect this dictionary. The file may be inaccessible, corrupted, or incompatible. Open /jetdebug for technical details.",
                ex.ToString());
        }
    }

    private static DictionarySource Invalid(
        string zipPath,
        DictionarySourceOrigin origin,
        string userMessage,
        string technicalDetails,
        IEnumerable<string>? unreadableEntries = null)
        => new()
        {
            Title = Path.GetFileNameWithoutExtension(zipPath),
            FilePath = zipPath,
            Origin = origin,
            Status = DictionaryInspectionStatus.Invalid,
            ErrorMessage = userMessage,
            TechnicalDetails = technicalDetails,
            UnreadableEntries = unreadableEntries?.ToList() ?? []
        };

    private static DictionaryIndexMetadata ReadIndex(
        ZipArchiveEntry indexEntry,
        string zipPath)
    {
        using var stream = indexEntry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "Yomitan index.json must contain a JSON object.");
        }

        return new DictionaryIndexMetadata(
            ReadValueAsString(
                root,
                "title",
                Path.GetFileNameWithoutExtension(zipPath)),
            ReadValueAsString(root, "revision", string.Empty),
            ReadValueAsString(root, "format", string.Empty));
    }

    private static string BuildPartialReadMessage(
        int problemCount,
        bool currentlySupported)
    {
        var noun = problemCount == 1 ? "entry" : "entries";
        var prefix = currentlySupported
            ? "JETHelper can use the readable portions of this dictionary, but"
            : "JETHelper recognized this dictionary, but";

        return $"{prefix} {problemCount} archive {noun} could not be read. "
               + "The archive may be corrupted, malformed, incomplete, or incompatible. "
               + "Download a fresh copy or remove it from the dictionary folder. "
               + "Open /jetdebug for technical details.";
    }

    private static string BuildTechnicalDetails(
        YomitanArchiveValidationResult validation,
        string summary)
    {
        var details = validation.Problems.Select(
            problem => $"{problem.EntryName}: {problem.Details}");

        return summary
               + Environment.NewLine
               + string.Join(Environment.NewLine, details);
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

    private static DictionaryDataKind DetectDataKinds(
        ZipArchive zip,
        ISet<string> unreadableEntries)
    {
        var kinds = DictionaryDataKind.None;

        if (HasReadableBanks(zip, "term_bank_", unreadableEntries))
            kinds |= DictionaryDataKind.TermDefinitions;

        if (HasReadableBanks(zip, "kanji_bank_", unreadableEntries))
            kinds |= DictionaryDataKind.KanjiDefinitions;

        if (HasReadableBanks(zip, "kanji_meta_bank_", unreadableEntries))
            kinds |= DictionaryDataKind.OtherKanjiMetadata;

        var termMetaBanks = GetReadableBanks(
                zip,
                "term_meta_bank_",
                unreadableEntries)
            .ToList();

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

            if ((kinds & (DictionaryDataKind.TermFrequency
                          | DictionaryDataKind.PitchAccent
                          | DictionaryDataKind.OtherTermMetadata)) == 0)
            {
                kinds |= DictionaryDataKind.OtherTermMetadata;
            }
        }

        return kinds;
    }

    private static bool HasRecognizedBankNames(ZipArchive zip)
        => zip.Entries.Any(entry =>
            IsBankEntry(entry.Name));

    private static bool HasReadableBanks(
        ZipArchive zip,
        string prefix,
        ISet<string> unreadableEntries)
        => GetReadableBanks(zip, prefix, unreadableEntries).Any();

    private static IEnumerable<ZipArchiveEntry> GetReadableBanks(
        ZipArchive zip,
        string prefix,
        ISet<string> unreadableEntries)
        => zip.Entries.Where(entry =>
            entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && entry.Name.EndsWith(
                ".json",
                StringComparison.OrdinalIgnoreCase)
            && !unreadableEntries.Contains(entry.FullName));

    private static bool IsBankEntry(string entryName)
        => entryName.EndsWith(
               ".json",
               StringComparison.OrdinalIgnoreCase)
           && entryName.Contains(
               "_bank_",
               StringComparison.OrdinalIgnoreCase);

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
                    {
                        continue;
                    }

                    var type = row[1].ValueKind == JsonValueKind.String
                        ? row[1].GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(type))
                        types.Add(type);
                }
            }
            catch
            {
                // Full validation already recorded the entry-level failure.
                // This remains defensive in case the file changes between
                // inspection passes.
            }
        }

        return types;
    }

    private static DictionaryContentRole DetectContentRole(string title)
    {
        if (ContainsAny(
                title,
                "JMnedict",
                "proper name",
                "names",
                "人名",
                "固有名",
                "姓名"))
        {
            return DictionaryContentRole.Names;
        }

        if (ContainsAny(
                title,
                "Kirei",
                "slang",
                "anime",
                "manga",
                "俗語",
                "若者言葉"))
        {
            return DictionaryContentRole.SlangOrMedia;
        }

        return DictionaryContentRole.General;
    }

    private static DictionaryLanguageKind DetectDefinitionLanguage(
        ZipArchive zip,
        string title,
        DictionaryContentRole role,
        ISet<string> unreadableEntries)
    {
        var titleLanguage = DetectTitleLanguage(title);
        if (titleLanguage != DictionaryLanguageKind.Unknown)
            return titleLanguage;

        if (role is DictionaryContentRole.Names
            && title.Contains(
                "JMnedict",
                StringComparison.OrdinalIgnoreCase))
        {
            return DictionaryLanguageKind.English;
        }

        var japaneseCharacters = 0;
        var latinCharacters = 0;
        var samplesSeen = 0;

        foreach (var bank in GetReadableBanks(
                     zip,
                     "term_bank_",
                     unreadableEntries)
                 .Take(2))
        {
            using var stream = bank.Open();
            using var document = JsonDocument.Parse(stream);

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array
                    || row.GetArrayLength() < 6)
                {
                    continue;
                }

                var text = ExtractGlossaryText(row[5]);
                CountLanguageCharacters(
                    text,
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
        if (ContainsAny(
                title,
                "(English)",
                " English",
                "JMdict",
                "KANJIDIC",
                "KireiCake",
                "和英",
                "英和"))
        {
            return DictionaryLanguageKind.English;
        }

        if (ContainsAny(
                title,
                "国語",
                "大辞林",
                "大辞泉",
                "広辞苑",
                "明鏡",
                "新明解",
                "故事",
                "ことわざ"))
        {
            return DictionaryLanguageKind.Japanese;
        }

        return DictionaryLanguageKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(
            term,
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
            {
                latinCharacters++;
            }
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
            JsonValueKind.Array => string.Join(
                " ",
                element.EnumerateArray().Select(ExtractGlossaryText)),
            JsonValueKind.Object => string.Join(
                " ",
                element.EnumerateObject().Select(property =>
                    ExtractGlossaryText(property.Value))),
            _ => string.Empty
        };

    private readonly record struct DictionaryIndexMetadata(
        string Title,
        string Revision,
        string Format);

}
