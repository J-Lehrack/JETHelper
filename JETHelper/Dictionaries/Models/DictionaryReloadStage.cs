namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Current lifecycle stage for dictionary discovery and archive inspection.
/// Parsing/index construction remains a separate follow-up phase.
/// </summary>
public enum DictionaryReloadStage
{
    NotStarted,
    Discovering,
    Validating,
    Ready,
    ReadyWithWarnings,
    Failed,
    Cancelled
}
