using System;
using Dalamud.Plugin.Services;

namespace JETHelper.Services;

/// <summary>
/// Watches for the plugin's lookup hotkey.
///
/// This service deliberately does not decide what lookup means.
/// Its only job is:
/// 1. Check the configured keys.
/// 2. Detect the transition from "not pressed" to "pressed".
/// 3. Tell Plugin.cs when the hotkey was triggered.
///
/// Keeping this tiny makes it easy to replace later if Dalamud adds a nicer keybind API
/// or if we build a richer configuration UI.
/// </summary>
public sealed class HotkeyService
{
    // Windows virtual-key codes for modifier keys.
    // Using integer VK codes keeps this compatible with Dalamud's IKeyState[int] indexer
    // without needing to rely on specific enum names.
    private const int VkShift = 0x10;
    private const int VkCtrl = 0x11;
    private const int VkAlt = 0x12;

    private readonly IKeyState keyState;
    private readonly Configuration configuration;
    private readonly Action onClipboardHotkeyPressed;

    // This tracks whether the full hotkey combination was down during the previous frame.
    // Without this, holding Shift+Y would process the clipboard every frame.
    private bool wasHotkeyDownLastFrame;

    public HotkeyService(
        IKeyState keyState,
        Configuration configuration,
        Action onClipboardHotkeyPressed)
    {
        this.keyState = keyState;
        this.configuration = configuration;
        this.onClipboardHotkeyPressed = onClipboardHotkeyPressed;
    }

    /// <summary>
    /// Called once per framework update by Plugin.cs.
    /// If the configured hotkey has just been pressed, this fires the callback once.
    /// </summary>
    public void Update()
    {
        if (!configuration.ClipboardHotkeyEnabled)
        {
            wasHotkeyDownLastFrame = false;
            return;
        }

        var isHotkeyDown = IsConfiguredHotkeyDown();

        // Edge detection: trigger only on the first frame where the hotkey becomes down.
        if (isHotkeyDown && !wasHotkeyDownLastFrame)
            onClipboardHotkeyPressed();

        wasHotkeyDownLastFrame = isHotkeyDown;
    }

    private bool IsConfiguredHotkeyDown()
    {
        if (!IsKeyDown(configuration.ClipboardHotkeyVirtualKey))
            return false;

        if (configuration.ClipboardHotkeyRequiresShift && !IsKeyDown(VkShift))
            return false;

        if (configuration.ClipboardHotkeyRequiresCtrl && !IsKeyDown(VkCtrl))
            return false;

        if (configuration.ClipboardHotkeyRequiresAlt && !IsKeyDown(VkAlt))
            return false;

        return true;
    }

    private bool IsKeyDown(int virtualKeyCode)
    {
        try
        {
            return keyState[virtualKeyCode];
        }
        catch (ArgumentException)
        {
            // If the user somehow saves an invalid key code later, fail safely instead of crashing.
            return false;
        }
    }
}
