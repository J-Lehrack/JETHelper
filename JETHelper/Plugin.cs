using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using JETHelper.Windows;
using JETHelper.Anki.Services;
using JETHelper.Diagnostics.Windows;
using JETHelper.Diagnostics.Services;
using JETHelper.Lookup.Services;
using JETHelper.Input;

namespace JETHelper;

/// <summary>
/// Plugin is the main entry point for JETHelper.
///
/// Dalamud creates one instance of this class when the plugin loads.
/// This class wires together:
/// - Dalamud services, such as commands and logging.
/// - Our own services, such as lookup and clipboard handling.
/// - Our windows, such as the main lookup window and config window.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    // These properties are filled in by Dalamud because of the [PluginService]
    // attribute. The "null!" tells C# that Dalamud will assign these before we
    // use them.
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static ICommandManager CommandManager
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static IClientState ClientState
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static IPlayerState PlayerState
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static IDataManager DataManager
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static IPluginLog Log
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static IKeyState KeyState
    {
        get; private set;
    } = null!;
    [PluginService]
    internal static IFramework Framework
    {
        get; private set;
    } = null!;

    // Slash commands registered with Dalamud.
    // /jet opens the main window, or processes text if text follows the
    // command. /jetlookup always treats the command argument as lookup text.
    // /jetclip reads the current clipboard text and processes that.
    // /jetconfig opens the settings window.
    // Shift+Y does the same thing by default through HotkeyService.
    private const string MainCommandName = "/jet";
    private const string LookupCommandName = "/jetlookup";
    private const string ClipboardCommandName = "/jetclip";
    private const string ConfigCommandName = "/jetconfig";
    private const string CardConfigCommandName = "/jetcardconfig";
    private const string AcknowledgementsCommandName = "/jetabout";
    private const string DiagnosticsCommandName = "/jetdebug";

    public Configuration Configuration { get; init; }

    // Services are small classes that own specific pieces of logic.
    // Keeping them separate prevents Plugin.cs from becoming a giant catch-all
    // file.
    public DiagnosticService DiagnosticService { get; private set; }
    public LookupService LookupService { get; private set; }
    public ClipboardService ClipboardService { get; } = new();
    public HotkeyService HotkeyService { get; private set; } = null!;
    public AnkiService AnkiService { get; private set; }

    // WindowSystem is Dalamud's manager for plugin windows.
    // We add our windows to it once, then Dalamud asks it to draw every frame.
    public readonly WindowSystem WindowSystem = new("JETHelper");

    private ConfigWindow ConfigWindow { get; init; }
    private CardConfigWindow CardConfigWindow { get; init; }
    private AcknowledgementsWindow AcknowledgementsWindow { get; init; }
    private DiagnosticsWindow DiagnosticsWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        // Load saved plugin settings. If no settings exist yet, create
        // defaults.
        Configuration = PluginInterface.GetPluginConfig() as Configuration
                        ?? new Configuration();

        // Diagnostics are created before the lookup pipeline so dictionary
        // discovery, loading, and later services can record technical details
        // without exposing them in normal user-facing messages.
        DiagnosticService = new DiagnosticService(Configuration,
                                                  PluginInterface,
                                                  Log);

        // Create the lookup pipeline after configuration is loaded so services
        // can read settings such as the manually configured dictionary folder
        // path.
        LookupService = new LookupService(Configuration, DiagnosticService);
        AnkiService = new AnkiService(DiagnosticService);

        // When the configured key combination is pressed, it calls
        // ProcessClipboardText().
        HotkeyService = new HotkeyService(KeyState,
                                          Configuration,
                                          ProcessClipboardText);

        // Create windows and register them with the window system.
        ConfigWindow = new ConfigWindow(this);
        CardConfigWindow = new CardConfigWindow(ConfigWindow);
        AcknowledgementsWindow = new AcknowledgementsWindow();
        DiagnosticsWindow = new DiagnosticsWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(CardConfigWindow);
        WindowSystem.AddWindow(AcknowledgementsWindow);
        WindowSystem.AddWindow(DiagnosticsWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register slash commands with Dalamud.
        CommandManager.AddHandler(
                  MainCommandName,
                  new CommandInfo(OnMainCommand)
                  {
                      HelpMessage = "Open the JETHelper lookup window. Add "
                                    + "text after the command to process it."
                  });

        CommandManager.AddHandler(
                  LookupCommandName,
                  new CommandInfo(OnLookupCommand)
                  {
                      HelpMessage = "Process the text after the command. "
                                    + "Example: /jetlookup 食べる"
                  });

        CommandManager.AddHandler(
                  ClipboardCommandName,
                  new CommandInfo(OnClipboardCommand)
                  {
                      HelpMessage
                      = "Process the current clipboard text. Example flow: "
                        + "copy Japanese text, then run /jetclip"
                  });

        CommandManager.AddHandler(
                  ConfigCommandName,
                  new CommandInfo(OnConfigCommand)
                  {
                      HelpMessage = "Open the JETHelper settings window."
                  });

        CommandManager.AddHandler(
                  CardConfigCommandName,
                  new CommandInfo(OnCardConfigCommand)
                  {
                      HelpMessage
                      = "Open the JETHelper Anki card field-mapping window."
                  });

        CommandManager.AddHandler(
                  AcknowledgementsCommandName,
                  new CommandInfo(OnAcknowledgementsCommand)
                  {
                      HelpMessage = "Open JETHelper acknowledgements, "
                                    + "licences, and data-source information."
                  });

        CommandManager.AddHandler(
                  DiagnosticsCommandName,
                  new CommandInfo(OnDiagnosticsCommand)
                  {
                      HelpMessage
                      = "Open JETHelper diagnostics, service health, "
                        + "and local log controls."
                  });

        // Tell Dalamud what to call when UI is drawn or when the user opens
        // plugin UI/config.
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Framework.Update runs repeatedly while the game/plugin is active.
        // We use it to poll key state for our hotkey.
        Framework.Update += OnFrameworkUpdate;

        Log.Information($"{PluginInterface.Manifest.Name} loaded.");
    }

    public void Dispose()
    {
        // Anything we subscribe/register in the constructor should be
        // unsubscribed/unregistered here. This helps avoid stale event handlers
        // after plugin reloads.
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Framework.Update -= OnFrameworkUpdate;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        CardConfigWindow.Dispose();
        AcknowledgementsWindow.Dispose();
        DiagnosticsWindow.Dispose();
        MainWindow.Dispose();
        AnkiService.Dispose();
        LookupService.Dispose();
        DiagnosticService.Dispose();

        CommandManager.RemoveHandler(MainCommandName);
        CommandManager.RemoveHandler(LookupCommandName);
        CommandManager.RemoveHandler(ClipboardCommandName);
        CommandManager.RemoveHandler(ConfigCommandName);
        CommandManager.RemoveHandler(CardConfigCommandName);
        CommandManager.RemoveHandler(AcknowledgementsCommandName);
        CommandManager.RemoveHandler(DiagnosticsCommandName);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        HotkeyService.Update();
    }

    private void OnMainCommand(string command, string args)
    {
        // If the user typed "/jet 食べる", process the argument directly.
        // If they typed only "/jet", open/close the lookup window.
        if (!string.IsNullOrWhiteSpace(args))
        {
            ProcessLookupText(args);
            return;
        }

        MainWindow.Toggle();
    }

    private void OnLookupCommand(string command, string args)
    {
        ProcessLookupText(args);
    }

    private void OnClipboardCommand(string command, string args)
    {
        ProcessClipboardText();
    }

    private void OnConfigCommand(string command, string args)
    {
        ConfigWindow.IsOpen = true;
    }

    private void OnCardConfigCommand(string command, string args)
    {
        OpenCardConfigUi();
    }

    private void OnAcknowledgementsCommand(string command, string args)
    {
        OpenAcknowledgementsUi();
    }

    private void OnDiagnosticsCommand(string command, string args)
    {
        OpenDiagnosticsUi();
    }

    /// <summary>
    /// Processes any raw string as lookup text, updates the main window, and
    /// opens it. This is the central path used by commands, buttons, and later
    /// hotkeys.
    /// </summary>
    public void ProcessLookupText(string? text)
    {
        var result = LookupService.ProcessRawText(
                  text, source: "Manual/command input");
        MainWindow.SetLookupResult(result);
        MainWindow.IsOpen = true;
    }

    /// <summary>
    /// Reads text from the clipboard only when the user explicitly requests it.
    /// We do not constantly monitor the clipboard, and we do not modify
    /// clipboard contents.
    /// </summary>
    public void ProcessClipboardText()
    {
        var clipboardText = ClipboardService.GetText();
        var result = LookupService.ProcessRawText(clipboardText,
                                                  source: "Clipboard");
        MainWindow.SetLookupResult(result);
        MainWindow.IsOpen = true;
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void OpenCardConfigUi() => CardConfigWindow.IsOpen = true;
    public void
    OpenAcknowledgementsUi() => AcknowledgementsWindow.IsOpen = true;
    public void OpenDiagnosticsUi() => DiagnosticsWindow.IsOpen = true;
    public void ToggleMainUi() => MainWindow.Toggle();
}
