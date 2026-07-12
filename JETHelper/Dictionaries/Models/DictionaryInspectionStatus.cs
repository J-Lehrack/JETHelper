namespace JETHelper.Dictionaries.Models;

/// <summary>
/// Result of inspecting a dictionary archive before lookup services load it.
/// </summary>
public enum DictionaryInspectionStatus
{
    Ready,
    ReadyWithWarnings,
    Unsupported,
    Invalid
}
