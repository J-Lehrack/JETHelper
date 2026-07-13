namespace JETHelper.Anki.Models;

/// <summary>
/// Result returned after an optional JETHelper note-type installation
/// attempt. Existing compatible note types count as successful because they
/// can be selected without JETHelper overwriting the user's templates or
/// styling.
/// </summary>
public sealed record AnkiTemplateInstallResult(
    bool Success,
    bool CreatedNoteType,
    bool ExistingNoteType,
    string Message,
    string NoteTypeName)
{
    public static AnkiTemplateInstallResult Created(
        string message,
        string noteTypeName)
        => new(
            Success: true,
            CreatedNoteType: true,
            ExistingNoteType: false,
            Message: message,
            NoteTypeName: noteTypeName);

    public static AnkiTemplateInstallResult Existing(
        string message,
        string noteTypeName)
        => new(
            Success: true,
            CreatedNoteType: false,
            ExistingNoteType: true,
            Message: message,
            NoteTypeName: noteTypeName);

    public static AnkiTemplateInstallResult Failed(
        string message,
        string noteTypeName)
        => new(
            Success: false,
            CreatedNoteType: false,
            ExistingNoteType: false,
            Message: message,
            NoteTypeName: noteTypeName);
}
