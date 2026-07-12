namespace JETHelper.Anki.Models;

/// <summary>
/// The Anki note target the user wants to create.
/// Actual AnkiConnect export will use this later to choose deck/model/field
/// mapping.
/// </summary>
public enum AnkiCardType { Vocabulary, Kanji }
