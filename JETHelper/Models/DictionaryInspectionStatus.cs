namespace JETHelper.Models;

/// <summary>
/// Result of inspecting a dictionary archive before any lookup service loads it.
/// </summary>
public enum DictionaryInspectionStatus
{
    Ready,
    Unsupported,
    Invalid
}
