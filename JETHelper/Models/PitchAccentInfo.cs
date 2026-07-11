namespace JETHelper.Models;

/// <summary>
/// Pitch accent data for a vocabulary entry.
/// For now this is a placeholder model so the UI and Anki payload have a stable shape.
/// </summary>
public sealed class PitchAccentInfo
{
    public string DisplayText { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;

    public bool HasValue => !string.IsNullOrWhiteSpace(DisplayText);
}
