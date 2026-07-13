using System.Collections.Generic;

namespace JETHelper.Anki.Models;

/// <summary>
/// Result returned after JETHelper attempts to create or confirm its two
/// optional recommended decks.
/// </summary>
public sealed
          record AnkiDeckCreationResult(bool Success,
                                        string Message,
                                        IReadOnlyList<string> ReadyDeckNames)
{
    public static AnkiDeckCreationResult
    Ok(string message, IReadOnlyList<string> readyDeckNames) => new(
              Success: true, Message: message, ReadyDeckNames: readyDeckNames);

    public static AnkiDeckCreationResult
    Failed(string message, IReadOnlyList<string> readyDeckNames) => new(
              Success: false, Message: message, ReadyDeckNames: readyDeckNames);
}
