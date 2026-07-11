namespace JETHelper.Models;

/// <summary>
/// One vocabulary candidate found inside the user's original lookup text.
///
/// A candidate can be an exact surface-form match, such as 日本語 -> 日本語,
/// or a deinflected match, such as 食べました -> 食べる.
/// The original surface text is kept so the UI can later explain why a base
/// form was suggested without losing the copied sentence/context.
/// </summary>
public sealed class LookupCandidate
{
    public string Text { get; init; } = string.Empty;
    public string SurfaceText { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int StartIndex { get; init; }
    public int SurfaceLength { get; init; }
    public bool IsDeinflected { get; init; }
    public bool IsLikely { get; init; }

    public string DisplayText
        => IsDeinflected && !string.IsNullOrWhiteSpace(SurfaceText) && SurfaceText != Text
               ? $"{Text} ← {SurfaceText}"
               : Text;

    public string TooltipText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Reason))
                return DisplayText;

            return $"{DisplayText}\n{Reason}";
        }
    }
}
