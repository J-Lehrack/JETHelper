using Dalamud.Configuration;
using System;

namespace JETHelper;

/// <summary>
/// Stores settings that should persist after the plugin reloads.
/// Dalamud serializes this object when Save() is called.
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool ClipboardHotkeyEnabled { get; set; } = true;
    public int ClipboardHotkeyVirtualKey { get; set; } = 0x59;
    public bool ClipboardHotkeyRequiresShift { get; set; } = true;
    public bool ClipboardHotkeyRequiresCtrl { get; set; } = false;
    public bool ClipboardHotkeyRequiresAlt { get; set; } = false;

    /// <summary>
    /// Optional manually selected folder containing dictionary ZIP files.
    /// Leave blank to use automatic discovery.
    /// </summary>
    public string DictionaryFolderPath { get; set; } = string.Empty;

    public string AnkiConnectUrl { get; set; } = "http://127.0.0.1:8765";

    /// <summary>
    /// Exact Anki deck and note type names selected from AnkiConnect.
    /// They intentionally start empty because other users will not have the
    /// developer's personal decks or note types.
    /// </summary>
    public string VocabularyDeckName { get; set; } = string.Empty;
    public string VocabularyNoteTypeName { get; set; } = string.Empty;
    public string KanjiDeckName { get; set; } = string.Empty;
    public string KanjiNoteTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Records which note type the current field mappings belong to. This lets
    /// a refresh preserve intentional choices such as "Do not export" while
    /// still rebuilding mappings when the user selects a different note type.
    /// </summary>
    public string VocabularyMappingNoteTypeName { get; set; } = string.Empty;
    public string KanjiMappingNoteTypeName { get; set; } = string.Empty;

    // Vocabulary semantic-role -> Anki field mappings.
    public string VocabularyExpressionField { get; set; } = string.Empty;
    public string VocabularyFuriganaField { get; set; } = string.Empty;
    public string VocabularyMeaningEnglishField { get; set; } = string.Empty;
    public string VocabularyMeaningJapaneseField { get; set; } = string.Empty;
    public string VocabularyMeaningSlangField { get; set; } = string.Empty;
    public string VocabularyAudioField { get; set; } = string.Empty;
    public string VocabularyFrequencyField { get; set; } = string.Empty;
    public string VocabularySentenceField { get; set; } = string.Empty;
    public string VocabularyPitchAccentField { get; set; } = string.Empty;

    // Kanji semantic-role -> Anki field mappings.
    public string KanjiCharacterField { get; set; } = string.Empty;
    public string KanjiMeaningField { get; set; } = string.Empty;
    public string KanjiKunyomiField { get; set; } = string.Empty;
    public string KanjiOnyomiField { get; set; } = string.Empty;
    public string KanjiFrequencyField { get; set; } = string.Empty;
    public string KanjiSentenceField { get; set; } = string.Empty;
    public string KanjiStrokesField { get; set; } = string.Empty;
    public string KanjiDiagramField { get; set; } = string.Empty;

    public void Save() { Plugin.PluginInterface.SavePluginConfig(this); }
}
