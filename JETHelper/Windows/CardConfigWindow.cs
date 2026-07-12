using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace JETHelper.Windows;

/// <summary>
/// Dedicated window for mapping JETHelper card data to fields in the user's
/// selected Anki note types.
///
/// This window owns all field-mapping presentation and interaction. The main
/// ConfigWindow continues to own AnkiConnect refreshes and deck/note-type
/// selection, then exposes the latest Anki metadata to this window.
/// </summary>
public sealed class CardConfigWindow : Window, IDisposable {
    private readonly ConfigWindow configWindow;
    private readonly Configuration configuration;

    public CardConfigWindow(ConfigWindow configWindow) :
          base("JETHelper Card Settings###JETHelperCardConfig")
    {
        this.configWindow = configWindow;
        configuration = configWindow.SharedConfiguration;

        SizeConstraints = new WindowSizeConstraints {
            // This width comfortably fits both mapping groups side-by-side.
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
        // No unmanaged resources yet.
    }

    public override void Draw()
    {
        var ankiResult = configWindow.LastAnkiResult;
        if (ankiResult is null || !ankiResult.Success) {
            ImGui.TextWrapped("Refresh AnkiConnect in the main JETHelper "
                              + "settings window "
                              + "before configuring card fields.");

            if (ImGui.Button("Open Main Settings"))
                configWindow.IsOpen = true;

            return;
        }

        ImGui.TextWrapped(
                  "Map each JETHelper data value to a field in the selected "
                  + "Anki "
                  + "note type. Required mappings are marked with *. Optional "
                  + "mappings may be set to Do not export.");
        ImGui.Spacing();

        // Always keep the two mapping groups horizontal for this version.
        // Each child receives half of the available content width.
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columnWidth = (ImGui.GetContentRegionAvail().X - spacing) / 2f;

        ImGui.BeginChild("VocabularyMappingPanel",
                         new Vector2(columnWidth, 0),
                         true);
        DrawFieldMappingSection("Vocabulary field mappings",
                                configuration.VocabularyNoteTypeName,
                                vocabulary: true);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("KanjiMappingPanel",
                         new Vector2(columnWidth, 0),
                         true);
        DrawFieldMappingSection("Kanji field mappings",
                                configuration.KanjiNoteTypeName,
                                vocabulary: false);
        ImGui.EndChild();
    }

    private void
    DrawFieldMappingSection(string heading, string modelName, bool vocabulary)
    {
        ImGui.TextUnformatted(heading);
        ImGui.Separator();
        ImGui.Spacing();

        var fields = configWindow.GetFieldsForNoteType(modelName);

        if (string.IsNullOrWhiteSpace(modelName)) {
            ImGui.TextDisabled(
                      "Select a note type in the main settings first.");
            return;
        }

        if (fields.Count == 0) {
            ImGui.TextDisabled(
                      "No fields were returned for this note type. Refresh "
                      + "AnkiConnect in the main settings.");
            return;
        }

        ImGui.PushID(vocabulary ? "VocabularyMappings" : "KanjiMappings");

        if (vocabulary)
            DrawVocabularyMappings(fields);
        else
            DrawKanjiMappings(fields);

        ImGui.PopID();
    }

    private void DrawVocabularyMappings(IReadOnlyList<string> fields)
    {
        DrawMappingCombo(
                  "Expression *",
                  configuration.VocabularyExpressionField,
                  fields,
                  value => configuration.VocabularyExpressionField = value);
        DrawMappingCombo(
                  "Furigana",
                  configuration.VocabularyFuriganaField,
                  fields,
                  value => configuration.VocabularyFuriganaField = value);
        DrawMappingCombo(
                  "Meaning English *",
                  configuration.VocabularyMeaningEnglishField,
                  fields,
                  value => configuration.VocabularyMeaningEnglishField = value);
        DrawMappingCombo("Meaning Japanese",
                         configuration.VocabularyMeaningJapaneseField,
                         fields,
                         value => configuration.VocabularyMeaningJapaneseField
                         = value);
        DrawMappingCombo(
                  "Meaning Slang",
                  configuration.VocabularyMeaningSlangField,
                  fields,
                  value => configuration.VocabularyMeaningSlangField = value);
        DrawMappingCombo("Audio",
                         configuration.VocabularyAudioField,
                         fields,
                         value => configuration.VocabularyAudioField = value);
        DrawMappingCombo(
                  "Frequency",
                  configuration.VocabularyFrequencyField,
                  fields,
                  value => configuration.VocabularyFrequencyField = value);
        DrawMappingCombo(
                  "Sentence",
                  configuration.VocabularySentenceField,
                  fields,
                  value => configuration.VocabularySentenceField = value);
        DrawMappingCombo(
                  "Pitch Accent",
                  configuration.VocabularyPitchAccentField,
                  fields,
                  value => configuration.VocabularyPitchAccentField = value);
    }

    private void DrawKanjiMappings(IReadOnlyList<string> fields)
    {
        DrawMappingCombo("Kanji Character *",
                         configuration.KanjiCharacterField,
                         fields,
                         value => configuration.KanjiCharacterField = value);
        DrawMappingCombo("Meaning *",
                         configuration.KanjiMeaningField,
                         fields,
                         value => configuration.KanjiMeaningField = value);
        DrawMappingCombo("Kunyomi",
                         configuration.KanjiKunyomiField,
                         fields,
                         value => configuration.KanjiKunyomiField = value);
        DrawMappingCombo("Onyomi",
                         configuration.KanjiOnyomiField,
                         fields,
                         value => configuration.KanjiOnyomiField = value);
        DrawMappingCombo("Frequency",
                         configuration.KanjiFrequencyField,
                         fields,
                         value => configuration.KanjiFrequencyField = value);
        DrawMappingCombo("Sentence",
                         configuration.KanjiSentenceField,
                         fields,
                         value => configuration.KanjiSentenceField = value);
        DrawMappingCombo("Strokes",
                         configuration.KanjiStrokesField,
                         fields,
                         value => configuration.KanjiStrokesField = value);
        DrawMappingCombo("Diagram",
                         configuration.KanjiDiagramField,
                         fields,
                         value => configuration.KanjiDiagramField = value);
    }

    private void DrawMappingCombo(string roleLabel,
                                  string currentValue,
                                  IReadOnlyList<string> fields,
                                  Action<string> onSelected)
    {
        ImGui.TextUnformatted(roleLabel);
        ImGui.SetNextItemWidth(-1);

        if (!ImGui.BeginCombo("##Mapping" + roleLabel,
                              EmptyDash(currentValue, "Do not export")))
            return;

        var noneSelected = string.IsNullOrWhiteSpace(currentValue);
        if (ImGui.Selectable("Do not export", noneSelected)) {
            onSelected(string.Empty);
            configuration.Save();
        }

        foreach (var field in fields) {
            var selected = string.Equals(field,
                                         currentValue,
                                         StringComparison.Ordinal);

            if (ImGui.Selectable(field, selected)) {
                onSelected(field);
                configuration.Save();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static string
    EmptyDash(string? value,
              string emptyText = "—") => string.IsNullOrWhiteSpace(value)
                                                   ? emptyText
                                                   : value;
}
