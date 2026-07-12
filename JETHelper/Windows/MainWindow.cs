using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup;
using JETHelper.Lookup.Models;
using System;
using System.Linq;
using System.Numerics;

namespace JETHelper.Windows;

/// <summary>
/// MainWindow draws the primary lookup interface.
///
/// Dalamud/ImGui windows are "immediate mode" UI. Draw() is called every frame,
/// and each frame we describe what the window should look like right now.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string inputText = string.Empty;
    private LookupResult currentResult = LookupResult.Empty();
    private string lastAnkiMessage = string.Empty;

    public MainWindow(Plugin plugin)
        : base("JETHelper Lookup###JETHelperMain")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
    }

    public void SetLookupResult(LookupResult result)
    {
        currentResult = result;
        inputText = result.CleanedText;
        lastAnkiMessage = string.Empty;
    }

    public override void Draw()
    {
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

        if (ImGui.Button("Process Text"))
        {
            currentResult = plugin.LookupService.ProcessRawText(inputText, source: "Window input");
            lastAnkiMessage = string.Empty;
        }

        ImGui.SameLine();

        if (ImGui.Button("Process Clipboard"))
        {
            var clipboardText = plugin.ClipboardService.GetText();
            currentResult = plugin.LookupService.ProcessRawText(clipboardText, source: "Clipboard");
            inputText = currentResult.CleanedText;
            lastAnkiMessage = string.Empty;
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear"))
        {
            inputText = string.Empty;
            currentResult = LookupResult.Empty();
            lastAnkiMessage = string.Empty;
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
            || (currentResult.VocabularyCard is null && currentResult.KanjiCard is null)
            || currentResult.StatusMessage.Contains("Could not", StringComparison.OrdinalIgnoreCase)
            || currentResult.StatusMessage.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(currentResult.StatusMessage);
        }
    }

    private void DrawAnkiStatusMessage()
    {
        if (string.IsNullOrWhiteSpace(lastAnkiMessage))
            return;

        ImGui.Spacing();
        ImGui.TextWrapped("Anki: " + lastAnkiMessage);
    }

    private void DrawVocabularyCandidate()
    {
        if (currentResult.VocabularyCard is not { } vocab)
        {
            if (currentResult.ContainsJapanese)
                ImGui.TextDisabled("No vocabulary-card candidate was found yet.");
            return;
        }

        if (!ImGui.CollapsingHeader("Vocabulary card candidate", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextUnformatted(vocab.Expression);
        ImGui.SameLine();
        ImGui.TextDisabled($"【{vocab.ReadingDisplay}】");

        DrawSmallLine("Frequency", vocab.Frequency.DisplayText);
        DrawSmallLine("Frequency source", vocab.Frequency.SourceDisplay);
        DrawSmallLine("Pitch", vocab.PitchAccent.HasValue ? vocab.PitchAccent.DisplayText : "—");
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

        if (ImGui.Button("Add Vocab Card"))
        {
            var result = plugin.AnkiService.AddVocabularyCard(plugin.Configuration, vocab);
            lastAnkiMessage = result.Message;
        }

        ImGui.SameLine();

        ImGui.BeginDisabled();
        ImGui.Button("More Details");
        ImGui.EndDisabled();
    }

    private void DrawKanjiCandidate()
    {
        if (currentResult.KanjiCard is not { } kanji)
        {
            if (currentResult.ContainsJapanese && currentResult.LookupText.Any(c => c >= '\u4e00' && c <= '\u9fff'))
                ImGui.TextDisabled("No kanji-card candidate was found yet.");
            return;
        }

        if (!ImGui.CollapsingHeader("Kanji card candidate", ImGuiTreeNodeFlags.DefaultOpen))
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

        if (ImGui.Button("Add Kanji Card"))
        {
            var result = plugin.AnkiService.AddKanjiCard(plugin.Configuration, kanji);
            lastAnkiMessage = result.Message;
        }

        ImGui.SameLine();

        ImGui.BeginDisabled();
        ImGui.Button("More Details");
        ImGui.EndDisabled();

        DrawAdditionalKanjiButtons();
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

    private void DrawVocabularyCandidateGroup(string label,
                                              System.Collections.Generic.List<LookupCandidate> candidates)
    {
        if (candidates.Count == 0)
            return;

        ImGui.TextDisabled(label);

        foreach (var candidate in candidates)
        {
            ImGui.SameLine();

            var buttonLabel = candidate.DisplayText + "##VocabularyCandidate" + candidate.Text + candidate.StartIndex + candidate.SurfaceLength;
            if (ImGui.SmallButton(buttonLabel))
            {
                currentResult = plugin.LookupService.FocusVocabularyCandidate(currentResult, candidate.Text);
                inputText = currentResult.CleanedText;
                lastAnkiMessage = string.Empty;
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

        foreach (var candidate in currentResult.AdditionalKanjiCandidates)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(candidate + "##AdditionalKanji" + candidate))
            {
                currentResult = plugin.LookupService.FocusKanjiCandidate(currentResult, candidate);
                inputText = currentResult.CleanedText;
                lastAnkiMessage = string.Empty;
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

        if (definitions.Count == 0)
        {
            var emptyMessage = label switch
            {
                "English" => "No English definition found.",
                "Japanese" => "No Japanese definition found.",
                "Slang / Anime" => "No slang or media-specific definition found.",
                _ => $"No {label.ToLowerInvariant()} definition found."
            };

            ImGui.TextDisabled(emptyMessage);

            if (sourcesSearched.Count > 0)
            {
                ImGui.TextDisabled("Sources searched:");
                ImGui.SameLine();
                ImGui.TextWrapped(string.Join(", ", sourcesSearched));
            }
            else
            {
                ImGui.TextDisabled($"No {label.ToLowerInvariant()} definition dictionaries are currently loaded.");
            }

            ImGui.TreePop();
            return;
        }

        foreach (var definition in definitions.Take(maxItems))
        {
            var source = definition.SourceDisplay;
            var pos = string.IsNullOrWhiteSpace(definition.PartOfSpeechDisplay)
                ? string.Empty
                : $" ({definition.PartOfSpeechDisplay})";

            ImGui.BulletText($"{definition.Meaning}{pos}");
            if (!string.IsNullOrWhiteSpace(source))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"[{source}]");
            }
        }

        if (definitions.Count > maxItems)
            ImGui.TextDisabled($"...plus {definitions.Count - maxItems} more definition(s).");

        ImGui.TreePop();
    }

    private static void DrawSmallLine(string label, string value)
    {
        ImGui.TextDisabled(label + ":");
        ImGui.SameLine();
        ImGui.TextWrapped(EmptyDash(value));
    }

    private static string EmptyDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
