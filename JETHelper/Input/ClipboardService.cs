using Dalamud.Bindings.ImGui;

namespace JETHelper.Input;

/// <summary>
/// Owns clipboard access for the plugin.
///
/// We only read clipboard text when the
/// user runs /jetclip or clicks the clipboard button. We do not watch the
/// clipboard, clear it, or write anything back to it.
///
/// ImGui exposes clipboard helpers that are safe and convenient inside Dalamud
/// UI code.
/// </summary>
public sealed class ClipboardService {
    public string GetText()
    {
        // ImGui.GetClipboardText() may return null if the clipboard has no
        // text. The ?? operator means: "if the left side is null, use the right
        // side instead."
        return ImGui.GetClipboardText() ?? string.Empty;
    }
}
