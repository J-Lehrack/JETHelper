using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace JETHelper.Dictionaries.Inspection;

/// <summary>
/// Performs a complete structural read of a Yomitan archive.
///
/// Reading every entry forces ZIP decompression/CRC checks. JSON entries are
/// parsed, and Yomitan bank files are required to contain arrays. Problems are
/// returned to the inspector rather than thrown so readable banks can remain
/// usable beside a damaged entry.
/// </summary>
internal static class YomitanArchiveValidator {
    public static YomitanArchiveValidationResult Validate(ZipArchive zip)
    {
        var problems = new List<YomitanArchiveEntryProblem>();

        foreach (var entry in zip.Entries) {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            try {
                using var stream = entry.Open();

                if (entry.Name.EndsWith(".json",
                                        StringComparison.OrdinalIgnoreCase)) {
                    using var document = JsonDocument.Parse(stream);

                    if (entry.Name.Equals("index.json",
                                          StringComparison.OrdinalIgnoreCase)
                        && document.RootElement.ValueKind
                                     != JsonValueKind.Object) {
                        throw new JsonException(
                                  "index.json must contain a JSON object.");
                    }

                    if (IsBankEntry(entry.Name)
                        && document.RootElement.ValueKind
                                     != JsonValueKind.Array) {
                        throw new JsonException(
                                  $"{entry.Name} must contain a JSON array.");
                    }
                }
                else {
                    // Reading the entire stream forces decompression and CRC
                    // validation for non-JSON media or metadata files.
                    stream.CopyTo(Stream.Null);
                }
            }
            catch (Exception ex) {
                problems.Add(new YomitanArchiveEntryProblem(entry.FullName,
                                                            ex.ToString()));
            }
        }

        return new YomitanArchiveValidationResult(problems);
    }

    private static bool
    IsBankEntry(string entryName) => entryName.EndsWith(
                                               ".json",
                                               StringComparison
                                                         .OrdinalIgnoreCase)
                                     && entryName.Contains(
                                               "_bank_",
                                               StringComparison
                                                         .OrdinalIgnoreCase);
}

internal sealed record
YomitanArchiveValidationResult(List<YomitanArchiveEntryProblem> Problems);

internal sealed record YomitanArchiveEntryProblem(string EntryName,
                                                  string Details);
