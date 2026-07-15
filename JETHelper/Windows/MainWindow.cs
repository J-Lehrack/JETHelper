using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using JETHelper.Anki.Models;
using JETHelper.Anki.Services;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup;
using JETHelper.Lookup.Models;
using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace JETHelper.Windows;

/// <summary>
/// MainWindow draws the primary lookup interface.
///
/// Dalamud/ImGui windows are "immediate mode" UI. Draw() is called every frame,
/// and each frame we describe what the window should look like right now.
/// </summary>
public sealed class MainWindow : Window, IDisposable {
    private readonly Plugin plugin;
    private readonly object ankiOperationLock = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private string inputText = string.Empty;
    private LookupResult currentResult = LookupResult.Empty();
    private string lastVocabularyAnkiMessage = string.Empty;
    private string lastKanjiAnkiMessage = string.Empty;
    private bool disposed;
    private bool vocabularyAddInProgress;
    private bool kanjiAddInProgress;
    private AnkiAddResult? pendingVocabularyAddResult;
    private AnkiAddResult? pendingKanjiAddResult;
    private int vocabularyMessageGeneration;
    private int kanjiMessageGeneration;

    public MainWindow(Plugin plugin) :
          base("JETHelper Lookup###JETHelperMain")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
        lock (ankiOperationLock) disposed = true;

        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }

    public void SetLookupResult(LookupResult result)
    {
        currentResult = result;
        inputText = result.CleanedText;
        ClearAllAnkiMessages();
    }

    public override void Draw()
    {
        ApplyPendingAnkiResults();
        DrawLookupInput();
        DrawLookupFeedback();
        DrawAnkiStatusMessage();
        ImGui.Spacing();

        DrawVocabularyCandidate();
        DrawKanjiCandidate();
    }

    private void DrawLookupInput()
    {
        ImGui.Text("Lookup text");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##LookupInput", ref inputText, 512);

        if (ImGui.Button("Process Text")) {
            currentResult = plugin.LookupService.ProcessRawText(
                      inputText, source: "Window input");
            ClearAllAnkiMessages();
        }

        ImGui.SameLine();

        if (ImGui.Button("Process Clipboard")) {
            var clipboardText = plugin.ClipboardService.GetText();
            currentResult = plugin.LookupService.ProcessRawText(
                      clipboardText, source: "Clipboard");
            inputText = currentResult.CleanedText;
            ClearAllAnkiMessages();
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear")) {
            inputText = string.Empty;
            currentResult = LookupResult.Empty();
            ClearAllAnkiMessages();
        }
    }

    private void DrawLookupFeedback()
    {
        if (currentResult.TextKind == JapaneseTextKind.Empty)
            return;

        // Successful lookups are represented by the card sections themselves.
        // Only show a message when the user needs to take action or when no
        // result could be produced.
        if (!currentResult.ContainsJapanese
            || (currentResult.VocabularyCard is null
                && currentResult.KanjiCard is null)
            || currentResult.StatusMessage.Contains(
                      "Could not", StringComparison.OrdinalIgnoreCase)
            || currentResult.StatusMessage.Contains(
                      "error", StringComparison.OrdinalIgnoreCase)) {
            ImGui.Spacing();
            ImGui.TextWrapped(currentResult.StatusMessage);
        }
    }

    private void DrawAnkiStatusMessage()
    {
        var operationState = GetAnkiAddOperationState();

        if (operationState.VocabularyInProgress) {
            ImGui.Spacing();
            ImGui.TextDisabled("Anki vocabulary: Adding card...");
        }
        else if (!string.IsNullOrWhiteSpace(lastVocabularyAnkiMessage)) {
            ImGui.Spacing();
            ImGui.TextWrapped("Anki vocabulary: " + lastVocabularyAnkiMessage);
        }

        if (operationState.KanjiInProgress) {
            ImGui.Spacing();
            ImGui.TextDisabled("Anki kanji: Adding card...");
        }
        else if (!string.IsNullOrWhiteSpace(lastKanjiAnkiMessage)) {
            ImGui.Spacing();
            ImGui.TextWrapped("Anki kanji: " + lastKanjiAnkiMessage);
        }
    }

    private void DrawVocabularyCandidate()
    {
        if (currentResult.VocabularyCard is not { } vocab) {
            if (currentResult.ContainsJapanese)
                ImGui.TextDisabled(
                          "No vocabulary-card candidate was found yet.");
            return;
        }

        if (!ImGui.CollapsingHeader("Vocabulary card candidate",
                                    ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextUnformatted(vocab.Expression);
        ImGui.SameLine();
        ImGui.TextDisabled($"【{vocab.ReadingDisplay}】");

        DrawSmallLine("Frequency", vocab.Frequency.DisplayText);
        DrawSmallLine("Frequency source", vocab.Frequency.SourceDisplay);
        DrawSmallLine("Pitch",
                      vocab.PitchAccent.HasValue ? vocab.PitchAccent.DisplayText
                                                 : "—");
        DrawSmallLine("Audio", EmptyDash(vocab.Audio));
        DrawSmallLine("Sentence/context", vocab.Sentence);

        DrawDefinitionSection("English",
                              vocab.EnglishDefinitions,
                              vocab.EnglishDefinitionSources,
                              maxItems: 6);
        DrawDefinitionSection("Japanese",
                              vocab.JapaneseDefinitions,
                              vocab.JapaneseDefinitionSources,
                              maxItems: 3);
        DrawDefinitionSection("Slang / Anime",
                              vocab.SlangDefinitions,
                              vocab.SlangDefinitionSources,
                              maxItems: 4);

        DrawSmallLine("Tags", string.Join(", ", vocab.Tags));

        DrawVocabularyCandidateButtons();

        var ankiState = GetAnkiAddOperationState();
        if (ankiState.VocabularyInProgress)
            ImGui.BeginDisabled();

        var addVocabularyLabel = ankiState.VocabularyInProgress
                                           ? "Adding Vocab Card..."
                                           : "Add Vocab Card";
        if (ImGui.Button(addVocabularyLabel))
            StartVocabularyCardAdd(vocab);

        if (ankiState.VocabularyInProgress)
            ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled();
        ImGui.Button("More Details");
        ImGui.EndDisabled();
    }

    private void DrawKanjiCandidate()
    {
        if (currentResult.KanjiCard is not { } kanji) {
            if (currentResult.ContainsJapanese
                && currentResult.LookupText.Any(c => c >= '\u4e00'
                                                     && c <= '\u9fff'))
                ImGui.TextDisabled("No kanji-card candidate was found yet.");
            return;
        }

        if (!ImGui.CollapsingHeader("Kanji card candidate",
                                    ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextUnformatted(kanji.KanjiCharacter);
        DrawSmallLine("Meanings", kanji.MeaningsDisplay);
        DrawSmallLine("Kunyomi", kanji.KunyomiDisplay);
        DrawSmallLine("Onyomi", kanji.OnyomiDisplay);
        DrawSmallLine("Frequency", kanji.Frequency.DisplayText);
        DrawSmallLine("Frequency source", kanji.Frequency.SourceDisplay);
        DrawSmallLine("Strokes", kanji.StrokeDisplay);
        DrawSmallLine("Diagram", EmptyDash(kanji.Diagram));
        DrawSmallLine("Sentence/context", kanji.Sentence);
        DrawSmallLine("Sources", string.Join(", ", kanji.SourceDictionaries));
        DrawSmallLine("Tags", string.Join(", ", kanji.Tags));

        var ankiState = GetAnkiAddOperationState();
        if (ankiState.KanjiInProgress)
            ImGui.BeginDisabled();

        var addKanjiLabel = ankiState.KanjiInProgress ? "Adding Kanji Card..."
                                                      : "Add Kanji Card";
        if (ImGui.Button(addKanjiLabel))
            StartKanjiCardAdd(kanji);

        if (ankiState.KanjiInProgress)
            ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled();
        ImGui.Button("More Details");
        ImGui.EndDisabled();

        DrawAdditionalKanjiButtons();
    }

    private void StartVocabularyCardAdd(VocabularyCardData vocab)
    {
        int messageGeneration;

        lock (ankiOperationLock)
        {
            if (disposed || vocabularyAddInProgress)
                return;

            vocabularyAddInProgress = true;
            lastVocabularyAnkiMessage = string.Empty;
            pendingVocabularyAddResult = null;
            messageGeneration = vocabularyMessageGeneration;
        }

        _ = RunVocabularyCardAddAsync(vocab, messageGeneration);
    }

    private async Task RunVocabularyCardAddAsync(VocabularyCardData vocab,
                                                 int messageGeneration)
    {
        AnkiAddResult result;

        try {
            result = await plugin.AnkiService.AddVocabularyCardAsync(
                      plugin.Configuration, vocab, lifetimeCancellation.Token);
        }
        catch (Exception ex) {
            plugin.DiagnosticService.Error("AnkiConnect",
                                           "Unexpected exception escaped the "
                                           + "asynchronous vocabulary "
                                                     + "card workflow.",
                                           ex);
            result = AnkiAddResult.Fail("Could not add card to Anki: "
                                        + ex.Message);
        }

        lock (ankiOperationLock)
        {
            vocabularyAddInProgress = false;
            if (!disposed && messageGeneration == vocabularyMessageGeneration)
                pendingVocabularyAddResult = result;
        }
    }

    private void StartKanjiCardAdd(KanjiCardData kanji)
    {
        int messageGeneration;

        lock (ankiOperationLock)
        {
            if (disposed || kanjiAddInProgress)
                return;

            kanjiAddInProgress = true;
            lastKanjiAnkiMessage = string.Empty;
            pendingKanjiAddResult = null;
            messageGeneration = kanjiMessageGeneration;
        }

        _ = RunKanjiCardAddAsync(kanji, messageGeneration);
    }

    private async Task RunKanjiCardAddAsync(KanjiCardData kanji,
                                            int messageGeneration)
    {
        AnkiAddResult result;

        try {
            result = await plugin.AnkiService.AddKanjiCardAsync(
                      plugin.Configuration, kanji, lifetimeCancellation.Token);
        }
        catch (Exception ex) {
            plugin.DiagnosticService.Error("AnkiConnect",
                                           "Unexpected exception escaped the "
                                           + "asynchronous kanji card "
                                                     + "workflow.",
                                           ex);
            result = AnkiAddResult.Fail("Could not add card to Anki: "
                                        + ex.Message);
        }

        lock (ankiOperationLock)
        {
            kanjiAddInProgress = false;
            if (!disposed && messageGeneration == kanjiMessageGeneration)
                pendingKanjiAddResult = result;
        }
    }

    private void ApplyPendingAnkiResults()
    {
        AnkiAddResult? vocabularyResult;
        AnkiAddResult? kanjiResult;

        lock (ankiOperationLock)
        {
            vocabularyResult = pendingVocabularyAddResult;
            pendingVocabularyAddResult = null;
            kanjiResult = pendingKanjiAddResult;
            pendingKanjiAddResult = null;
        }

        if (vocabularyResult is not null)
            lastVocabularyAnkiMessage = vocabularyResult.Message;

        if (kanjiResult is not null)
            lastKanjiAnkiMessage = kanjiResult.Message;
    }

    private AnkiAddOperationState GetAnkiAddOperationState()
    {
        lock (ankiOperationLock) return new AnkiAddOperationState(
                  vocabularyAddInProgress, kanjiAddInProgress);
    }

    private void ClearAllAnkiMessages()
    {
        lastVocabularyAnkiMessage = string.Empty;
        lastKanjiAnkiMessage = string.Empty;

        // A completed or still-running result belongs to the old lookup
        // context and should not reappear after the user processes new text.
        lock (ankiOperationLock)
        {
            pendingVocabularyAddResult = null;
            pendingKanjiAddResult = null;
            vocabularyMessageGeneration++;
            kanjiMessageGeneration++;
        }
    }

    private void ClearVocabularyAnkiMessage()
    {
        lastVocabularyAnkiMessage = string.Empty;

        // Changing only the vocabulary candidate should not erase the kanji
        // result. Incrementing the generation also suppresses a late result
        // from a vocabulary request that started for the previous candidate.
        lock (ankiOperationLock)
        {
            pendingVocabularyAddResult = null;
            vocabularyMessageGeneration++;
        }
    }

    private void ClearKanjiAnkiMessage()
    {
        lastKanjiAnkiMessage = string.Empty;

        // Changing only the kanji candidate should not erase the vocabulary
        // result. Incrementing the generation also suppresses a late result
        // from a kanji request that started for the previous candidate.
        lock (ankiOperationLock)
        {
            pendingKanjiAddResult = null;
            kanjiMessageGeneration++;
        }
    }

    private void DrawVocabularyCandidateButtons()
    {
        if (currentResult.VocabularyCandidateDetails.Count == 0)
            return;

        ImGui.Spacing();

        var likely = currentResult.VocabularyCandidateDetails
                               .Where(c => c.IsLikely)
                               .ToList();
        var possible = currentResult.VocabularyCandidateDetails
                                 .Where(c => !c.IsLikely)
                                 .ToList();

        DrawVocabularyCandidateGroup("Likely vocabulary candidates:", likely);
        DrawVocabularyCandidateGroup("Possible substring matches:", possible);
    }

    private void DrawVocabularyCandidateGroup(
              string label,
              System.Collections.Generic.List<LookupCandidate> candidates)
    {
        if (candidates.Count == 0)
            return;

        ImGui.TextDisabled(label);

        foreach (var candidate in candidates) {
            ImGui.SameLine();

            var buttonLabel = candidate.DisplayText + "##VocabularyCandidate"
                              + candidate.Text + candidate.StartIndex
                              + candidate.SurfaceLength;
            if (ImGui.SmallButton(buttonLabel)) {
                currentResult = plugin.LookupService.FocusVocabularyCandidate(
                          currentResult, candidate.Text);
                inputText = currentResult.CleanedText;
                ClearVocabularyAnkiMessage();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(candidate.TooltipText);
        }
    }

    private void DrawAdditionalKanjiButtons()
    {
        if (currentResult.AdditionalKanjiCandidates.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextDisabled("Additional kanji candidates:");

        foreach (var candidate in currentResult.AdditionalKanjiCandidates) {
            ImGui.SameLine();
            if (ImGui.SmallButton(candidate + "##AdditionalKanji"
                                  + candidate)) {
                currentResult = plugin.LookupService.FocusKanjiCandidate(
                          currentResult, candidate);
                inputText = currentResult.CleanedText;
                ClearKanjiAnkiMessage();
            }
        }
    }

    private static void DrawDefinitionSection(
              string label,
              System.Collections.Generic.List<DictionaryDefinition> definitions,
              System.Collections.Generic.IReadOnlyList<string> sourcesSearched,
              int maxItems)
    {
        if (!ImGui.TreeNodeEx(label))
            return;

        if (definitions.Count == 0) {
            var emptyMessage = label switch {
                "English" => "No English definition found.",
                "Japanese" => "No Japanese definition found.",
                "Slang / Anime" => "No slang or media-specific definition "
                                   + "found.",
                _ => $"No {label.ToLowerInvariant()} definition found."
            };

            ImGui.TextDisabled(emptyMessage);

            if (sourcesSearched.Count > 0) {
                ImGui.TextDisabled("Sources searched:");
                ImGui.SameLine();
                ImGui.TextWrapped(string.Join(", ", sourcesSearched));
            }
            else {
                ImGui.TextDisabled(
                          $"No {label.ToLowerInvariant()} definition dictionaries are currently loaded.");
            }

            ImGui.TreePop();
            return;
        }

        foreach (var definition in definitions.Take(maxItems)) {
            var source = definition.SourceDisplay;
            var pos = string.IsNullOrWhiteSpace(definition.PartOfSpeechDisplay)
                                ? string.Empty
                                : $" ({definition.PartOfSpeechDisplay})";

            ImGui.BulletText($"{definition.Meaning}{pos}");
            if (!string.IsNullOrWhiteSpace(source)) {
                ImGui.SameLine();
                ImGui.TextDisabled($"[{source}]");
            }
        }

        if (definitions.Count > maxItems)
            ImGui.TextDisabled(
                      $"...plus {definitions.Count - maxItems} more definition(s).");

        ImGui.TreePop();
    }

    private static void DrawSmallLine(string label, string value)
    {
        ImGui.TextDisabled(label + ":");
        ImGui.SameLine();
        ImGui.TextWrapped(EmptyDash(value));
    }

    private sealed record AnkiAddOperationState(bool VocabularyInProgress,
                                                bool KanjiInProgress);

    private static string EmptyDash(
              string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
