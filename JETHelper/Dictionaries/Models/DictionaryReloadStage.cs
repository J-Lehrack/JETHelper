namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Current lifecycle stage for dictionary discovery, validation, and index
/// construction.
/// </summary>
public enum DictionaryReloadStage {
    NotStarted,
    Discovering,
    Validating,
    Indexing,
    Ready,
    ReadyWithWarnings,
    Failed,
    Cancelled
}
