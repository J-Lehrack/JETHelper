using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using JETHelper.Anki.Services;

namespace JETHelper.Windows;

/// <summary>
/// Draws the plugin settings window.
/// </summary>
public class ConfigWindow : Window, IDisposable {
    private static readonly(
              string Name,
              int VirtualKey)[] HotkeyOptions = BuildHotkeyOptions();

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private string dictionaryFolderPath;
    private string ankiConnectUrl;
    private AnkiConnectionResult? lastAnkiResult;

    public ConfigWindow(Plugin plugin) :
          base("JETHelper Settings###JETHelperConfig")
    {
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(500, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        configuration = plugin.Configuration;
        dictionaryFolderPath = configuration.DictionaryFolderPath;
        ankiConnectUrl = configuration.AnkiConnectUrl;
    }

    public void Dispose()
    {
        // No unmanaged resources yet.
    }

    public override void Draw()
    {
        DrawHotkeySettings();
        ImGui.Separator();
        DrawDictionarySettings();
        ImGui.Separator();
        DrawAnkiSettings();
        ImGui.Separator();
        DrawDiagnosticsSettings();
        ImGui.Separator();
        DrawAcknowledgementsSettings();
    }

    private void DrawHotkeySettings()
    {
        ImGui.TextWrapped("Hotkey settings. Default: Shift + Y processes the "
                          + "clipboard.");
        ImGui.Spacing();

        var enabled = configuration.ClipboardHotkeyEnabled;
        if (ImGui.Checkbox("Enable clipboard hotkey", ref enabled)) {
            configuration.ClipboardHotkeyEnabled = enabled;
            configuration.Save();
        }

        ImGui.TextUnformatted("Modifiers");
        ImGui.SameLine();

        var requiresCtrl = configuration.ClipboardHotkeyRequiresCtrl;
        if (ImGui.Checkbox("Ctrl", ref requiresCtrl)) {
            configuration.ClipboardHotkeyRequiresCtrl = requiresCtrl;
            configuration.Save();
        }

        ImGui.SameLine();
        var requiresAlt = configuration.ClipboardHotkeyRequiresAlt;
        if (ImGui.Checkbox("Alt", ref requiresAlt)) {
            configuration.ClipboardHotkeyRequiresAlt = requiresAlt;
            configuration.Save();
        }

        ImGui.SameLine();
        var requiresShift = configuration.ClipboardHotkeyRequiresShift;
        if (ImGui.Checkbox("Shift", ref requiresShift)) {
            configuration.ClipboardHotkeyRequiresShift = requiresShift;
            configuration.Save();
        }

        ImGui.TextUnformatted("Lookup key");
        ImGui.SameLine();

        // Change this width to adjust the visible size of the key dropdown.
        // For example, 120 is narrower and 200 is wider.
        ImGui.SetNextItemWidth(150f);

        var currentKeyName = GetHotkeyName(
                  configuration.ClipboardHotkeyVirtualKey);
        if (ImGui.BeginCombo("##ClipboardLookupKey", currentKeyName)) {
            foreach (var option in HotkeyOptions) {
                var selected = option.VirtualKey
                               == configuration.ClipboardHotkeyVirtualKey;
                if (ImGui.Selectable(option.Name, selected)) {
                    configuration.ClipboardHotkeyVirtualKey = option.VirtualKey;
                    configuration.Save();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled($"Current hotkey: {BuildHotkeyDisplayText()}");
    }

    private void DrawDictionarySettings()
    {
        ImGui.TextWrapped("JETHelper always checks its bundled dictionary "
                          + "folder. Set an additional folder here for "
                          + "user-supplied Yomitan dictionaries, or leave it "
                          + "blank to use bundled dictionaries only.");
        ImGui.Spacing();

        ImGui.TextUnformatted("Dictionary folder path");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##DictionaryFolderPath",
                        ref dictionaryFolderPath,
                        1024);

        if (ImGui.Button("Save Dictionary Path")) {
            configuration.DictionaryFolderPath = dictionaryFolderPath.Trim()
                                                           .Trim('"');
            configuration.Save();
            plugin.LookupService.ReloadDictionaries();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Dictionary Path")) {
            dictionaryFolderPath = string.Empty;
            configuration.DictionaryFolderPath = string.Empty;
            configuration.Save();
            plugin.LookupService.ReloadDictionaries();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reload Dictionaries"))
            plugin.LookupService.ReloadDictionaries();

        var savedPath = configuration.DictionaryFolderPath;
        var exists = !string.IsNullOrWhiteSpace(savedPath)
                     && Directory.Exists(savedPath);
        ImGui.TextDisabled($"Saved path: {EmptyDash(savedPath)}");
        ImGui.TextDisabled(
                  $"Saved path exists: {(string.IsNullOrWhiteSpace(savedPath) ? "—" : exists ? "Yes" : "No")}");

        var sources = plugin.LookupService.DictionarySources;
        var duplicateDecisions = plugin.LookupService
                                           .DictionaryDuplicateDecisions;
        var revisionGroups = plugin.LookupService.DictionaryRevisionGroups;
        var loaderErrors = plugin.LookupService.DictionaryLoaderErrors;
        var usableCount = sources.Count(source => source.IsUsable);
        var warningCount = sources.Count(source => source.HasWarnings);
        var problemCount = sources.Count(source => !source.IsUsable);
        var health = usableCount == 0 ? "Not ready"
                     : warningCount > 0 || problemCount > 0
                                         || duplicateDecisions.Count > 0
                                         || revisionGroups.Count > 0
                                         || loaderErrors.Count > 0
                               ? "Ready with warnings"
                               : "Ready";

        ImGui.TextDisabled(
                  $"Dictionary status: {health} ({usableCount} usable, "
                  + $"{warningCount} with warnings, "
                  + $"{problemCount} skipped/unsupported, "
                  + $"{loaderErrors.Count} load error(s))");

        if (loaderErrors.Count > 0) {
            ImGui.TextWrapped("Some dictionary banks failed while loading. "
                              + "Working sources remain available; open "
                              + "/jetdebug for technical details.");
        }

        if (ImGui.TreeNodeEx("Detected dictionary sources")) {
            if (sources.Count == 0) {
                ImGui.TextDisabled("No dictionary ZIP files were detected.");
            }
            else {
                foreach (var source in sources) {
                    var details
                              = source.IsUsable
                                          ? $"{source.Status}; {source.DataKinds}; "
                                                      + $"{source.Language}; {source.Origin}"
                                          : $"{source.Status}; {source.Origin}";

                    ImGui.BulletText(source.DisplayName);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({details})");
                    ImGui.TextDisabled("  File: " + source.FilePath);

                    if (!string.IsNullOrWhiteSpace(source.ErrorMessage))
                        ImGui.TextWrapped("  " + source.ErrorMessage);
                }
            }

            if (duplicateDecisions.Count > 0) {
                ImGui.Spacing();
                ImGui.TextUnformatted("Duplicate copies resolved");

                foreach (var decision in duplicateDecisions) {
                    ImGui.BulletText(decision.Preferred.DisplayName);
                    ImGui.TextWrapped("  Using: "
                                      + decision.Preferred.FilePath);
                    ImGui.TextWrapped(
                              "  Ignored: "
                              + string.Join(
                                        ", ",
                                        decision.Ignored.Select(
                                                  source => source.FilePath)));
                    ImGui.TextDisabled("  " + decision.Reason);
                }
            }

            if (revisionGroups.Count > 0) {
                ImGui.Spacing();
                ImGui.TextUnformatted("Multiple revisions loaded");
                ImGui.TextDisabled(
                          "Different revisions are treated as separate sources "
                          + "rather than silently replacing one another.");

                foreach (var group in revisionGroups) {
                    ImGui.BulletText(group.DisplayName);
                    foreach (var source in group.Sources) {
                        ImGui.TextDisabled(
                                  $"  Revision {EmptyDash(source.Revision)} — {source.Origin}: {source.FilePath}");
                    }
                }
            }

            ImGui.TreePop();
        }
    }

    private void DrawAnkiSettings()
    {
        ImGui.TextWrapped("Connect to Anki, select where vocabulary and kanji "
                          + "cards should be saved, then map JETHelper data to "
                          + "the fields used by each note type.");
        ImGui.Spacing();

        ImGui.TextUnformatted("AnkiConnect URL");
        ImGui.SetNextItemWidth(415f);
        ImGui.InputText("##AnkiConnectUrl", ref ankiConnectUrl, 256);

        if (ImGui.Button("Save Anki URL")) {
            configuration.AnkiConnectUrl = ankiConnectUrl.Trim();
            configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh / Test AnkiConnect")) {
            configuration.AnkiConnectUrl = ankiConnectUrl.Trim();
            configuration.Save();
            lastAnkiResult = plugin.AnkiService.TestConnection(configuration);

            if (lastAnkiResult.Success)
                ApplyFirstRunSelectionsAndMappings(lastAnkiResult);
        }

        ImGui.Spacing();

        var decks = lastAnkiResult?.DeckNames ?? [];
        var noteTypes = lastAnkiResult?.NoteTypeNames ?? [];

        if (lastAnkiResult is null) {
            ImGui.TextDisabled("Refresh AnkiConnect to populate deck, note "
                               + "type, and field dropdowns.");
            ImGui.TextWrapped(
                      "Requirement: Anki must be open and the AnkiConnect "
                      + "add-on must be installed and enabled.");
            return;
        }

        ImGui.TextWrapped(lastAnkiResult.Message);
        if (!lastAnkiResult.Success)
            return;

        if (decks.Count == 0)
            ImGui.TextWrapped("No Anki decks were found. Create a deck in "
                              + "Anki, then refresh this page.");

        // Vocabulary and kanji targets are parallel concepts, so present
        // them side-by-side. The table gives each side half of the available
        // width and keeps the vertical divider aligned across both rows.
        if (ImGui.BeginTable("AnkiTargets",
                             2,
                             ImGuiTableFlags.SizingStretchSame
                                       | ImGuiTableFlags.BordersInnerV)) {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawSelectableCombo("Vocabulary deck",
                                configuration.VocabularyDeckName,
                                decks,
                                selected =>
                                {
                                    configuration.VocabularyDeckName = selected;
                                    configuration.Save();
                                });

            ImGui.TableSetColumnIndex(1);
            DrawSelectableCombo("Kanji deck",
                                configuration.KanjiDeckName,
                                decks,
                                selected =>
                                {
                                    configuration.KanjiDeckName = selected;
                                    configuration.Save();
                                });

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            DrawSelectableCombo(
                      "Vocabulary note type",
                      configuration.VocabularyNoteTypeName,
                      noteTypes,
                      selected =>
                      {
                          configuration.VocabularyNoteTypeName = selected;
                          InitializeVocabularyMappingsForNewModel(
                                    selected, GetModelFields(selected));
                          configuration.Save();
                      });

            ImGui.TableSetColumnIndex(1);
            DrawSelectableCombo("Kanji note type",
                                configuration.KanjiNoteTypeName,
                                noteTypes,
                                selected =>
                                {
                                    configuration.KanjiNoteTypeName = selected;
                                    InitializeKanjiMappingsForNewModel(
                                              selected,
                                              GetModelFields(selected));
                                    configuration.Save();
                                });

            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ImGui.Button("Open Card Field Mappings"))
            plugin.OpenCardConfigUi();

        ImGui.SameLine();
        ImGui.TextDisabled("Also available with /jetcardconfig");
    }

    private void DrawDiagnosticsSettings()
    {
        ImGui.TextWrapped("Open service health, recent troubleshooting events, "
                          + "and controls "
                          + "for the local JETHelper.log file.");

        if (ImGui.Button("Open Diagnostics"))
            plugin.OpenDiagnosticsUi();

        ImGui.SameLine();
        ImGui.TextDisabled("Also available with /jetdebug");
    }

    private void DrawAcknowledgementsSettings()
    {
        ImGui.TextWrapped(
                  "View acknowledgements, licences, bundled dictionary "
                  + "sources, "
                  + "and links to the official project and licence pages.");

        if (ImGui.Button("Open Acknowledgements"))
            plugin.OpenAcknowledgementsUi();

        ImGui.SameLine();
        ImGui.TextDisabled("Also available with /jetabout");
    }

    /// <summary>
    /// Keeps valid saved selections, but gives a new user sensible defaults.
    /// Decks prefer Anki's Default deck. Note types are auto-selected only when
    /// their standard fields prove that they are compatible.
    /// </summary>
    private void ApplyFirstRunSelectionsAndMappings(AnkiConnectionResult result)
    {
        configuration.VocabularyDeckName = ChooseDeck(
                  configuration.VocabularyDeckName, result.DeckNames);
        configuration.KanjiDeckName = ChooseDeck(configuration.KanjiDeckName,
                                                 result.DeckNames);

        configuration.VocabularyNoteTypeName = ChooseCompatibleModel(
                  configuration.VocabularyNoteTypeName,
                  result,
                  ["Expression", "Meaning English"]);
        configuration.KanjiNoteTypeName = ChooseCompatibleModel(
                  configuration.KanjiNoteTypeName,
                  result,
                  ["Kanji Character", "Meaning"]);

        PreserveOrInitializeVocabularyMappings(
                  configuration.VocabularyNoteTypeName,
                  GetModelFields(configuration.VocabularyNoteTypeName));
        PreserveOrInitializeKanjiMappings(
                  configuration.KanjiNoteTypeName,
                  GetModelFields(configuration.KanjiNoteTypeName));
        configuration.Save();
    }

    private static string ChooseDeck(string current,
                                     IReadOnlyList<string> decks)
    {
        if (decks.Contains(current, StringComparer.Ordinal))
            return current;

        var defaultDeck = decks.FirstOrDefault(
                  deck => string.Equals(
                            deck, "Default", StringComparison.Ordinal));
        return defaultDeck ?? decks.FirstOrDefault() ?? string.Empty;
    }

    private static string
    ChooseCompatibleModel(string current,
                          AnkiConnectionResult result,
                          IReadOnlyCollection<string> standardRequiredFields)
    {
        if (result.NoteTypeNames.Contains(current, StringComparer.Ordinal))
            return current;

        return result.NoteTypeNames.FirstOrDefault(
                         model =>
                         {
                             if (!result.ModelFields.TryGetValue(
                                           model, out var fields))
                                 return false;

                             return standardRequiredFields.All(
                                       required => fields.Contains(
                                                 required,
                                                 StringComparer.Ordinal));
                         })
               ?? string.Empty;
    }

    private IReadOnlyList<string> GetModelFields(string modelName)
    {
        if (lastAnkiResult is null || string.IsNullOrWhiteSpace(modelName)
            || !lastAnkiResult.ModelFields.TryGetValue(modelName,
                                                       out var fields))
            return [];

        return fields;
    }

    private void
    PreserveOrInitializeVocabularyMappings(string modelName,
                                           IReadOnlyList<string> fields)
    {
        if (string.IsNullOrWhiteSpace(
                      configuration.VocabularyMappingNoteTypeName)
            && HasAnyVocabularyMapping()) {
            // Migration path for configurations saved before mapping ownership
            // was tracked. Existing user choices are assumed to belong to the
            // currently selected note type, then validated below.
            configuration.VocabularyMappingNoteTypeName = modelName;
        }
        else if (!string.Equals(configuration.VocabularyMappingNoteTypeName,
                                modelName,
                                StringComparison.Ordinal)) {
            InitializeVocabularyMappingsForNewModel(modelName, fields);
            return;
        }

        // Empty means the user intentionally selected "Do not export". Keep it.
        // A non-empty target is cleared only if that field no longer exists.
        configuration.VocabularyExpressionField = KeepValidMapping(
                  configuration.VocabularyExpressionField, fields);
        configuration.VocabularyFuriganaField = KeepValidMapping(
                  configuration.VocabularyFuriganaField, fields);
        configuration.VocabularyMeaningEnglishField = KeepValidMapping(
                  configuration.VocabularyMeaningEnglishField, fields);
        configuration.VocabularyMeaningJapaneseField = KeepValidMapping(
                  configuration.VocabularyMeaningJapaneseField, fields);
        configuration.VocabularyMeaningSlangField = KeepValidMapping(
                  configuration.VocabularyMeaningSlangField, fields);
        configuration.VocabularyAudioField = KeepValidMapping(
                  configuration.VocabularyAudioField, fields);
        configuration.VocabularyFrequencyField = KeepValidMapping(
                  configuration.VocabularyFrequencyField, fields);
        configuration.VocabularySentenceField = KeepValidMapping(
                  configuration.VocabularySentenceField, fields);
        configuration.VocabularyPitchAccentField = KeepValidMapping(
                  configuration.VocabularyPitchAccentField, fields);
    }

    private void PreserveOrInitializeKanjiMappings(string modelName,
                                                   IReadOnlyList<string> fields)
    {
        if (string.IsNullOrWhiteSpace(configuration.KanjiMappingNoteTypeName)
            && HasAnyKanjiMapping()) {
            configuration.KanjiMappingNoteTypeName = modelName;
        }
        else if (!string.Equals(configuration.KanjiMappingNoteTypeName,
                                modelName,
                                StringComparison.Ordinal)) {
            InitializeKanjiMappingsForNewModel(modelName, fields);
            return;
        }

        configuration.KanjiCharacterField = KeepValidMapping(
                  configuration.KanjiCharacterField, fields);
        configuration.KanjiMeaningField = KeepValidMapping(
                  configuration.KanjiMeaningField, fields);
        configuration.KanjiKunyomiField = KeepValidMapping(
                  configuration.KanjiKunyomiField, fields);
        configuration.KanjiOnyomiField = KeepValidMapping(
                  configuration.KanjiOnyomiField, fields);
        configuration.KanjiFrequencyField = KeepValidMapping(
                  configuration.KanjiFrequencyField, fields);
        configuration.KanjiSentenceField = KeepValidMapping(
                  configuration.KanjiSentenceField, fields);
        configuration.KanjiStrokesField = KeepValidMapping(
                  configuration.KanjiStrokesField, fields);
        configuration.KanjiDiagramField = KeepValidMapping(
                  configuration.KanjiDiagramField, fields);
    }

    private bool HasAnyVocabularyMapping()
    {
        return !string.IsNullOrWhiteSpace(
                         configuration.VocabularyExpressionField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularyFuriganaField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularyMeaningEnglishField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularyMeaningJapaneseField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularyMeaningSlangField)
               || !string.IsNullOrWhiteSpace(configuration.VocabularyAudioField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularyFrequencyField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularySentenceField)
               || !string.IsNullOrWhiteSpace(
                         configuration.VocabularyPitchAccentField);
    }

    private bool HasAnyKanjiMapping()
    {
        return !string.IsNullOrWhiteSpace(configuration.KanjiCharacterField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiMeaningField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiKunyomiField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiOnyomiField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiFrequencyField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiSentenceField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiStrokesField)
               || !string.IsNullOrWhiteSpace(configuration.KanjiDiagramField);
    }

    private void
    InitializeVocabularyMappingsForNewModel(string modelName,
                                            IReadOnlyList<string> fields)
    {
        configuration.VocabularyExpressionField = ExactFieldOrEmpty(
                  "Expression", fields);
        configuration.VocabularyFuriganaField = ExactFieldOrEmpty("Furigana",
                                                                  fields);
        configuration.VocabularyMeaningEnglishField = ExactFieldOrEmpty(
                  "Meaning English", fields);
        configuration.VocabularyMeaningJapaneseField = ExactFieldOrEmpty(
                  "Meaning Japanese", fields);
        configuration.VocabularyMeaningSlangField = ExactFieldOrEmpty(
                  "Meaning Slang", fields);
        configuration.VocabularyAudioField = ExactFieldOrEmpty("Audio", fields);
        configuration.VocabularyFrequencyField = ExactFieldOrEmpty("Frequency",
                                                                   fields);
        configuration.VocabularySentenceField = ExactFieldOrEmpty("Sentence",
                                                                  fields);
        configuration.VocabularyPitchAccentField = ExactFieldOrEmpty(
                  "Pitch Accent", fields);
        configuration.VocabularyMappingNoteTypeName = modelName;
    }

    private void
    InitializeKanjiMappingsForNewModel(string modelName,
                                       IReadOnlyList<string> fields)
    {
        configuration.KanjiCharacterField = ExactFieldOrEmpty("Kanji Character",
                                                              fields);
        configuration.KanjiMeaningField = ExactFieldOrEmpty("Meaning", fields);
        configuration.KanjiKunyomiField = ExactFieldOrEmpty("Kunyomi", fields);
        configuration.KanjiOnyomiField = ExactFieldOrEmpty("Onyomi", fields);
        configuration.KanjiFrequencyField = ExactFieldOrEmpty("Frequency",
                                                              fields);
        configuration.KanjiSentenceField = ExactFieldOrEmpty("Sentence",
                                                             fields);
        configuration.KanjiStrokesField = ExactFieldOrEmpty("Strokes", fields);
        configuration.KanjiDiagramField = ExactFieldOrEmpty("Diagram", fields);
        configuration.KanjiMappingNoteTypeName = modelName;
    }

    private static string KeepValidMapping(string current,
                                           IReadOnlyList<string> fields)
    {
        if (string.IsNullOrWhiteSpace(current))
            return string.Empty;

        return fields.Contains(current, StringComparer.Ordinal) ? current
                                                                : string.Empty;
    }

    private static string ExactFieldOrEmpty(string standardFieldName,
                                            IReadOnlyList<string> fields)
    {
        return fields.Contains(standardFieldName, StringComparer.Ordinal)
                         ? standardFieldName
                         : string.Empty;
    }

    /// <summary>
    /// Exposes the most recent successful or failed Anki refresh result to the
    /// dedicated card-settings window. ConfigWindow still owns the connection
    /// refresh because deck and note-type selection live here.
    /// </summary>
    internal AnkiConnectionResult? LastAnkiResult => lastAnkiResult;

    /// <summary>
    /// Exposes the shared plugin configuration to CardConfigWindow. The card
    /// window owns field-mapping controls, while both windows save to the same
    /// Dalamud configuration object.
    /// </summary>
    internal Configuration SharedConfiguration => configuration;

    /// <summary>
    /// Returns the fields AnkiConnect reported for a selected note type.
    /// </summary>
    internal IReadOnlyList<string>
    GetFieldsForNoteType(string modelName) => GetModelFields(modelName);

    private static void DrawSelectableCombo(string label,
                                            string currentValue,
                                            IReadOnlyList<string> options,
                                            Action<string> onSelected)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1f);

        if (options.Count == 0) {
            ImGui.BeginDisabled();
            if (ImGui.BeginCombo("##" + label, EmptyDash(currentValue)))
                ImGui.EndCombo();
            ImGui.EndDisabled();
            return;
        }

        if (!ImGui.BeginCombo("##" + label, EmptyDash(currentValue)))
            return;

        foreach (var option in options.OrderBy(x => x)) {
            var isSelected = option == currentValue;
            if (ImGui.Selectable(option, isSelected))
                onSelected(option);

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private string BuildHotkeyDisplayText()
    {
        var parts = new List<string>();
        if (configuration.ClipboardHotkeyRequiresCtrl)
            parts.Add("Ctrl");
        if (configuration.ClipboardHotkeyRequiresAlt)
            parts.Add("Alt");
        if (configuration.ClipboardHotkeyRequiresShift)
            parts.Add("Shift");

        parts.Add(GetHotkeyName(configuration.ClipboardHotkeyVirtualKey));
        return string.Join(" + ", parts);
    }

    private static string GetHotkeyName(int virtualKey)
    {
        return HotkeyOptions
                         .FirstOrDefault(option => option.VirtualKey
                                                   == virtualKey)
                         .Name
               ?? $"VK 0x{virtualKey:X2}";
    }

    private static (string Name, int VirtualKey)[] BuildHotkeyOptions()
    {
        var options = new List<(string Name, int VirtualKey)>();

        for (var key = 'A'; key <= 'Z'; key++)
            options.Add((key.ToString(), key));

        for (var key = '0'; key <= '9'; key++)
            options.Add((key.ToString(), key));

        for (var index = 1; index <= 12; index++)
            options.Add(($"F{index}", 0x6F + index));

        options.AddRange([
            ("Space", 0x20),    ("Page Up", 0x21),     ("Page Down", 0x22),
            ("End", 0x23),      ("Home", 0x24),        ("Left Arrow", 0x25),
            ("Up Arrow", 0x26), ("Right Arrow", 0x27), ("Down Arrow", 0x28),
            ("Insert", 0x2D),   ("Delete", 0x2E),      ("Numpad 0", 0x60),
            ("Numpad 1", 0x61), ("Numpad 2", 0x62),    ("Numpad 3", 0x63),
            ("Numpad 4", 0x64), ("Numpad 5", 0x65),    ("Numpad 6", 0x66),
            ("Numpad 7", 0x67), ("Numpad 8", 0x68),    ("Numpad 9", 0x69)
        ]);

        return options.ToArray();
    }

    private static string
    EmptyDash(string? value,
              string emptyText = "—") => string.IsNullOrWhiteSpace(value)
                                                   ? emptyText
                                                   : value;
}
