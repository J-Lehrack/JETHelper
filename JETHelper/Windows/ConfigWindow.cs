using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using JETHelper.Anki.Models;
using JETHelper.Anki.Services;
using JETHelper.Anki.Templates;

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
    private readonly object ankiOperationLock = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private string dictionaryFolderPath;
    private string ankiConnectUrl;
    private AnkiConnectionResult? lastAnkiResult;
    private string recommendedDeckStatus = string.Empty;
    private string vocabularyTemplateStatus = string.Empty;
    private string kanjiTemplateStatus = string.Empty;
    private bool disposed;
    private bool ankiRefreshInProgress;
    private bool recommendedDeckCreationInProgress;
    private bool vocabularyTemplateInstallInProgress;
    private bool kanjiTemplateInstallInProgress;
    private AnkiConnectionResult? pendingAnkiRefreshResult;
    private DeckCreationCompletion? pendingDeckCreation;
    private TemplateInstallCompletion? pendingVocabularyTemplateInstall;
    private TemplateInstallCompletion? pendingKanjiTemplateInstall;

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
        lock (ankiOperationLock) disposed = true;

        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    public override void Draw()
    {
        ApplyPendingAnkiOperationResults();
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
        ImGui.TextWrapped("Performance note: Large custom dictionaries can "
                          + "take tens of seconds to load and may temporarily "
                          + "require substantially more memory during reload "
                          + "while the current and replacement snapshots "
                          + "coexist. Add optional dictionaries gradually and "
                          + "avoid heavily overlapping sources.");
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

        var reloadStatus = plugin.LookupService.DictionaryReloadStatus;
        var sources = plugin.LookupService.DictionarySources;
        var duplicateDecisions = plugin.LookupService
                                           .DictionaryDuplicateDecisions;
        var revisionGroups = plugin.LookupService.DictionaryRevisionGroups;
        var loaderErrors = plugin.LookupService.DictionaryLoaderErrors;
        var usableCount = sources.Count(source => source.IsUsable);
        var warningCount = sources.Count(source => source.HasWarnings);
        var problemCount = sources.Count(source => !source.IsUsable);

        if (reloadStatus.IsActive) {
            var progress
                      = reloadStatus.TotalSources > 0
                                  ? $"{reloadStatus.ProcessedSources}/"
                                              + $"{reloadStatus.TotalSources}"
                                  : "—";
            var current = string.IsNullOrWhiteSpace(reloadStatus.CurrentSource)
                                    ? string.Empty
                                    : $" Current: "
                                                + reloadStatus.CurrentSource;

            ImGui.TextDisabled($"Dictionary reload: {reloadStatus.StageLabel} "
                               + $"({progress}).{current}");
            ImGui.TextWrapped(reloadStatus.Message);
        }
        else if (!string.IsNullOrWhiteSpace(reloadStatus.Message)) {
            ImGui.TextDisabled($"Dictionary reload: {reloadStatus.StageLabel}");
            ImGui.TextWrapped(reloadStatus.Message);
        }

        var health = reloadStatus.IsActive && usableCount == 0 ? "Loading"
                     : reloadStatus.IsActive
                               ? "Reloading; current snapshot active"
                     : usableCount == 0 ? "Not ready"
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
        var operationState = GetAnkiConfigOperationState();

        ImGui.TextWrapped("Connect to Anki, select where vocabulary and kanji "
                          + "cards should be saved, then map JETHelper data to "
                          + "the fields used by each note type.");
        ImGui.Spacing();

        ImGui.TextUnformatted("AnkiConnect URL");
        if (operationState.AnyActive)
            ImGui.BeginDisabled();

        ImGui.SetNextItemWidth(415f);
        ImGui.InputText("##AnkiConnectUrl", ref ankiConnectUrl, 256);

        if (ImGui.Button("Save Anki URL")) {
            configuration.AnkiConnectUrl = ankiConnectUrl.Trim();
            configuration.Save();
        }

        if (operationState.AnyActive)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (operationState.AnyActive)
            ImGui.BeginDisabled();

        var refreshButtonText = operationState.Refreshing
                                          ? "Connecting..."
                                          : "Refresh / Test AnkiConnect";
        if (ImGui.Button(refreshButtonText)) {
            configuration.AnkiConnectUrl = ankiConnectUrl.Trim();
            configuration.Save();
            StartAnkiRefresh();
        }

        if (operationState.AnyActive)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(operationState.StatusText))
            ImGui.TextDisabled(operationState.StatusText);

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
        if (operationState.AnyActive)
            ImGui.BeginDisabled();

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

        if (operationState.AnyActive)
            ImGui.EndDisabled();

        ImGui.Spacing();
        DrawOptionalJetHelperNoteTypes(operationState);

        ImGui.Spacing();
        if (operationState.AnyActive)
            ImGui.BeginDisabled();

        if (ImGui.Button("Open Card Field Mappings"))
            plugin.OpenCardConfigUi();

        if (operationState.AnyActive)
            ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextDisabled("Also available with /jetcardconfig");
    }

    private void
    DrawOptionalJetHelperNoteTypes(AnkiConfigOperationState operationState)
    {
        if (!ImGui.TreeNodeEx("Optional JETHelper note types"))
            return;

        ImGui.TextWrapped(
                  "JETHelper can create polished vocabulary and kanji note "
                  + "types "
                  + "using the field names already supported by the plugin. "
                  + "Installation is opt-in and never overwrites an existing "
                  + "note type's templates or styling.");

        ImGui.Spacing();
        ImGui.TextWrapped("Recommended decks are created independently from "
                          + "note types, so "
                          + "a note-type naming conflict cannot prevent deck "
                          + "creation.");

        if (operationState.AnyActive)
            ImGui.BeginDisabled();

        var deckButtonText = operationState.CreatingDecks
                                       ? "Creating Recommended Decks..."
                                       : "Create Recommended Decks";
        if (ImGui.Button(deckButtonText, new Vector2(-1f, 0f)))
            StartRecommendedDeckCreation();

        if (operationState.AnyActive)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(recommendedDeckStatus))
            ImGui.TextWrapped(recommendedDeckStatus);

        ImGui.TextDisabled(
                  $"Vocabulary deck: {JETHelperAnkiTemplates.VocabularyDeckName}");
        ImGui.TextDisabled(
                  $"Kanji deck: {JETHelperAnkiTemplates.KanjiDeckName}");

        ImGui.Spacing();

        if (ImGui.BeginTable("JETHelperNoteTypeInstallers",
                             2,
                             ImGuiTableFlags.SizingStretchSame
                                       | ImGuiTableFlags.BordersInnerV)) {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            if (operationState.AnyActive)
                ImGui.BeginDisabled();

            var vocabularyButtonText
                      = operationState.InstallingVocabulary
                                  ? "Installing Vocabulary Note Type..."
                                  : "Install Vocabulary Note Type";
            if (ImGui.Button(vocabularyButtonText, new Vector2(-1f, 0f)))
                StartTemplateInstallation(AnkiCardType.Vocabulary);

            if (operationState.AnyActive)
                ImGui.EndDisabled();

            if (!string.IsNullOrWhiteSpace(vocabularyTemplateStatus))
                ImGui.TextWrapped(vocabularyTemplateStatus);

            ImGui.TableSetColumnIndex(1);
            if (operationState.AnyActive)
                ImGui.BeginDisabled();

            var kanjiButtonText = operationState.InstallingKanji
                                            ? "Installing Kanji Note Type..."
                                            : "Install Kanji Note Type";
            if (ImGui.Button(kanjiButtonText, new Vector2(-1f, 0f)))
                StartTemplateInstallation(AnkiCardType.Kanji);

            if (operationState.AnyActive)
                ImGui.EndDisabled();

            if (!string.IsNullOrWhiteSpace(kanjiTemplateStatus))
                ImGui.TextWrapped(kanjiTemplateStatus);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.TextDisabled(
                  "Templates use native Anki HTML/CSS and conditional fields. "
                  + "No JavaScript, remote fonts, or external web resources "
                  + "are used.");

        ImGui.TreePop();
    }

    private void StartAnkiRefresh()
    {
        lock (ankiOperationLock)
        {
            if (disposed || IsAnyAnkiConfigOperationActiveUnsafe())
                return;

            ankiRefreshInProgress = true;
        }

        _ = RunAnkiRefreshAsync();
    }

    private async Task RunAnkiRefreshAsync()
    {
        AnkiConnectionResult result;

        try {
            result = await plugin.AnkiService.TestConnectionAsync(
                      configuration, lifetimeCancellation.Token);
        }
        catch (Exception ex) {
            plugin.DiagnosticService.Error("AnkiConnect",
                                           "Unexpected exception escaped the "
                                           + "asynchronous connection "
                                                     + "refresh workflow.",
                                           ex);
            result = CreateUnexpectedConnectionFailure(ex);
        }

        lock (ankiOperationLock)
        {
            ankiRefreshInProgress = false;
            if (!disposed)
                pendingAnkiRefreshResult = result;
        }
    }

    private void StartRecommendedDeckCreation()
    {
        lock (ankiOperationLock)
        {
            if (disposed || IsAnyAnkiConfigOperationActiveUnsafe())
                return;

            recommendedDeckCreationInProgress = true;
            recommendedDeckStatus = string.Empty;
        }

        _ = RunRecommendedDeckCreationAsync();
    }

    private async Task RunRecommendedDeckCreationAsync()
    {
        AnkiDeckCreationResult result;
        AnkiConnectionResult? refreshResult = null;

        try {
            result = await plugin.AnkiService
                               .CreateRecommendedJetHelperDecksAsync(
                                         configuration,
                                         lifetimeCancellation.Token);

            if (result.Success)
                refreshResult = await plugin.AnkiService.TestConnectionAsync(
                          configuration, lifetimeCancellation.Token);
        }
        catch (Exception ex) {
            plugin.DiagnosticService.Error("AnkiConnect",
                                           "Unexpected exception escaped the "
                                           + "asynchronous recommended "
                                                     + "deck workflow.",
                                           ex);
            result = AnkiDeckCreationResult.Failed(
                      "Could not create or confirm the recommended decks: "
                                + ex.Message,
                      []);
        }

        lock (ankiOperationLock)
        {
            recommendedDeckCreationInProgress = false;
            if (!disposed)
                pendingDeckCreation = new DeckCreationCompletion(result,
                                                                 refreshResult);
        }
    }

    private void StartTemplateInstallation(AnkiCardType cardType)
    {
        lock (ankiOperationLock)
        {
            if (disposed || IsAnyAnkiConfigOperationActiveUnsafe())
                return;

            if (cardType == AnkiCardType.Vocabulary) {
                vocabularyTemplateInstallInProgress = true;
                vocabularyTemplateStatus = string.Empty;
            }
            else {
                kanjiTemplateInstallInProgress = true;
                kanjiTemplateStatus = string.Empty;
            }
        }

        _ = RunTemplateInstallationAsync(cardType);
    }

    private async Task RunTemplateInstallationAsync(AnkiCardType cardType)
    {
        AnkiTemplateInstallResult result;
        AnkiConnectionResult? refreshResult = null;

        try {
            result = cardType == AnkiCardType.Vocabulary
                               ? await plugin.AnkiService
                                           .InstallJetHelperVocabularyNoteTypeAsync(
                                                     configuration,
                                                     lifetimeCancellation.Token)
                               : await plugin.AnkiService
                                           .InstallJetHelperKanjiNoteTypeAsync(
                                                     configuration,
                                                     lifetimeCancellation
                                                               .Token);

            if (result.Success)
                refreshResult = await plugin.AnkiService.TestConnectionAsync(
                          configuration, lifetimeCancellation.Token);
        }
        catch (Exception ex) {
            plugin.DiagnosticService.Error(
                      "AnkiConnect",
                      "Unexpected exception escaped the asynchronous optional "
                                + "note-type installation workflow.",
                      ex);
            result = AnkiTemplateInstallResult.Failed(
                      "Could not install the JETHelper note type: "
                                + ex.Message,
                      cardType == AnkiCardType.Vocabulary
                                ? JETHelperAnkiTemplates.Vocabulary.NoteTypeName
                                : JETHelperAnkiTemplates.Kanji.NoteTypeName);
        }

        var completion = new TemplateInstallCompletion(result,
                                                       refreshResult,
                                                       cardType);

        lock (ankiOperationLock)
        {
            if (cardType == AnkiCardType.Vocabulary) {
                vocabularyTemplateInstallInProgress = false;
                if (!disposed)
                    pendingVocabularyTemplateInstall = completion;
            }
            else {
                kanjiTemplateInstallInProgress = false;
                if (!disposed)
                    pendingKanjiTemplateInstall = completion;
            }
        }
    }

    private void ApplyPendingAnkiOperationResults()
    {
        AnkiConnectionResult? refreshResult;
        DeckCreationCompletion? deckCompletion;
        TemplateInstallCompletion? vocabularyCompletion;
        TemplateInstallCompletion? kanjiCompletion;

        lock (ankiOperationLock)
        {
            refreshResult = pendingAnkiRefreshResult;
            pendingAnkiRefreshResult = null;
            deckCompletion = pendingDeckCreation;
            pendingDeckCreation = null;
            vocabularyCompletion = pendingVocabularyTemplateInstall;
            pendingVocabularyTemplateInstall = null;
            kanjiCompletion = pendingKanjiTemplateInstall;
            pendingKanjiTemplateInstall = null;
        }

        if (refreshResult is not null) {
            lastAnkiResult = refreshResult;
            if (refreshResult.Success)
                ApplyFirstRunSelectionsAndMappings(refreshResult);
        }

        if (deckCompletion is not null)
            ApplyRecommendedDeckCompletion(deckCompletion);

        if (vocabularyCompletion is not null)
            ApplyInstalledTemplateSelection(vocabularyCompletion);

        if (kanjiCompletion is not null)
            ApplyInstalledTemplateSelection(kanjiCompletion);
    }

    private void
    ApplyRecommendedDeckCompletion(DeckCreationCompletion completion)
    {
        recommendedDeckStatus = completion.Result.Message;
        if (!completion.Result.Success)
            return;

        var refreshResult = completion.RefreshResult;
        if (refreshResult is null || !refreshResult.Success) {
            recommendedDeckStatus
                      += " The decks were created or confirmed, but JETHelper "
                         + "could not refresh Anki's deck list.";
            return;
        }

        lastAnkiResult = refreshResult;
        configuration.VocabularyDeckName = JETHelperAnkiTemplates
                                                     .VocabularyDeckName;
        configuration.KanjiDeckName = JETHelperAnkiTemplates.KanjiDeckName;
        configuration.Save();
        recommendedDeckStatus += " They are now selected for vocabulary and "
                                 + "kanji card export.";
    }

    private void
    ApplyInstalledTemplateSelection(TemplateInstallCompletion completion)
    {
        var installResult = completion.Result;
        var cardType = completion.CardType;

        if (cardType == AnkiCardType.Vocabulary)
            vocabularyTemplateStatus = installResult.Message;
        else
            kanjiTemplateStatus = installResult.Message;

        if (!installResult.Success)
            return;

        var refreshResult = completion.RefreshResult;
        if (refreshResult is null || !refreshResult.Success) {
            var refreshMessage = " The note type operation succeeded, but "
                                 + "JETHelper could not refresh Anki's deck "
                                 + "and field lists.";
            if (cardType == AnkiCardType.Vocabulary)
                vocabularyTemplateStatus += refreshMessage;
            else
                kanjiTemplateStatus += refreshMessage;

            return;
        }

        lastAnkiResult = refreshResult;

        if (cardType == AnkiCardType.Vocabulary) {
            configuration.VocabularyNoteTypeName = installResult.NoteTypeName;
            InitializeVocabularyMappingsForNewModel(
                      installResult.NoteTypeName,
                      GetModelFields(installResult.NoteTypeName));
        }
        else {
            configuration.KanjiNoteTypeName = installResult.NoteTypeName;
            InitializeKanjiMappingsForNewModel(
                      installResult.NoteTypeName,
                      GetModelFields(installResult.NoteTypeName));
        }

        configuration.Save();
    }

    private AnkiConfigOperationState GetAnkiConfigOperationState()
    {
        lock (ankiOperationLock)
        {
            return new AnkiConfigOperationState(
                      ankiRefreshInProgress,
                      recommendedDeckCreationInProgress,
                      vocabularyTemplateInstallInProgress,
                      kanjiTemplateInstallInProgress);
        }
    }

    private bool
    IsAnyAnkiConfigOperationActiveUnsafe() => ankiRefreshInProgress
                                              || recommendedDeckCreationInProgress
                                              || vocabularyTemplateInstallInProgress
                                              || kanjiTemplateInstallInProgress;

    private static AnkiConnectionResult
    CreateUnexpectedConnectionFailure(Exception ex) => new(
              Success: false,
              Message: "Could not connect to AnkiConnect: " + ex.Message,
              DeckNames: [],
              NoteTypeNames: [],
              ModelFields: new Dictionary<string, List<string>>(
                        StringComparer.Ordinal));

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

    private sealed record
    DeckCreationCompletion(AnkiDeckCreationResult Result,
                           AnkiConnectionResult? RefreshResult);

    private sealed record
    TemplateInstallCompletion(AnkiTemplateInstallResult Result,
                              AnkiConnectionResult? RefreshResult,
                              AnkiCardType CardType);

    private sealed record AnkiConfigOperationState(bool Refreshing,
                                                   bool CreatingDecks,
                                                   bool InstallingVocabulary,
                                                   bool InstallingKanji)
    {
        public bool AnyActive => Refreshing || CreatingDecks
                                 || InstallingVocabulary || InstallingKanji;

        public string
                  StatusText => Refreshing      ? "Connecting to AnkiConnect..."
                                : CreatingDecks ? "Creating recommended decks "
                                                  + "and refreshing Anki..."
                                : InstallingVocabulary
                                          ? "Installing the vocabulary note "
                                            + "type and refreshing Anki..."
                                : InstallingKanji
                                          ? "Installing the kanji note type "
                                            + "and refreshing Anki..."
                                          : string.Empty;
    }

    private static string
    EmptyDash(string? value,
              string emptyText = "—") => string.IsNullOrWhiteSpace(value)
                                                   ? emptyText
                                                   : value;
}
