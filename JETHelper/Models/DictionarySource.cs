using System;
using System.IO;

namespace JETHelper.Models;

/// <summary>
/// Metadata for one discovered dictionary archive.
///
/// The catalog inspects each ZIP once, then the lookup services select only the
/// sources they understand. This prevents filename-specific assumptions from
/// leaking throughout the dictionary layer.
/// </summary>
public sealed class DictionarySource
{
    public string Title { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public DictionarySourceOrigin Origin { get; init; }
    public DictionaryDataKind DataKinds { get; init; }
    public DictionaryLanguageKind Language { get; init; }
    public DictionaryContentRole ContentRole { get; init; }
    public DictionaryInspectionStatus Status { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Title)
                                     ? Path.GetFileNameWithoutExtension(FilePath)
                                     : Title;

    public bool HasKind(DictionaryDataKind kind)
        => (DataKinds & kind) == kind;

    /// <summary>
    /// Used to prevent the same logical dictionary from being loaded from both
    /// the bundled and user folders. Revision remains part of the identity so a
    /// newer user-supplied revision is not mistaken for the bundled revision.
    /// </summary>
    public string IdentityKey
        => string.Join("|",
                       DisplayName.Trim().ToUpperInvariant(),
                       Revision.Trim().ToUpperInvariant(),
                       ((int)DataKinds).ToString());

    public override string ToString()
        => $"{DisplayName} ({Origin}, {Status}, {DataKinds})";
}

/// <summary>
/// A ZIP path found by the path resolver before its contents are inspected.
/// </summary>
public readonly record struct DictionaryFileCandidate(
    string FilePath,
    DictionarySourceOrigin Origin);
