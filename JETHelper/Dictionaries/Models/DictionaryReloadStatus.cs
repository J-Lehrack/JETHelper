namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Immutable status snapshot exposed to the settings UI while dictionary
/// discovery, archive validation, parsing, and index construction run in the
/// background.
/// </summary>
public sealed record DictionaryReloadStatus
{
    public DictionaryReloadStage Stage { get; init; }
        = DictionaryReloadStage.NotStarted;

    public int ProcessedSources { get; init; }
    public int TotalSources { get; init; }
    public string CurrentSource { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool HasActiveSnapshot { get; init; }

    public bool IsActive
        => Stage is DictionaryReloadStage.Discovering
            or DictionaryReloadStage.Validating
            or DictionaryReloadStage.Indexing;

    public string StageLabel => Stage switch
    {
        DictionaryReloadStage.NotStarted => "Not started",
        DictionaryReloadStage.Discovering => "Discovering",
        DictionaryReloadStage.Validating => "Validating",
        DictionaryReloadStage.Indexing => "Indexing",
        DictionaryReloadStage.Ready => "Ready",
        DictionaryReloadStage.ReadyWithWarnings => "Ready with warnings",
        DictionaryReloadStage.Failed => "Failed",
        DictionaryReloadStage.Cancelled => "Cancelled",
        _ => Stage.ToString()
    };
}
