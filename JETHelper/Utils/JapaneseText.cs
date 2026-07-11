using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JETHelper.Utils;

public enum JapaneseTextKind
{
    Empty,
    NoJapanese,
    KanaOnly,
    KanjiOnly,
    MixedJapanese,
    MixedJapaneseAndNonJapanese
}

/// <summary>
/// Utility methods for cleaning and classifying Japanese text.
/// These methods deliberately avoid dictionary logic; they only answer questions
/// about the characters in the text.
/// </summary>
public static class JapaneseText
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string CleanForLookup(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        var normalized = rawText.Normalize(NormalizationForm.FormKC).Trim();
        normalized = WhitespaceRegex.Replace(normalized, " ");

        // Avoid accidentally processing huge clipboard contents.
        return normalized.Length <= 512 ? normalized : normalized[..512];
    }

    public static bool ContainsJapanese(string text)
        => text.EnumerateRunes().Any(IsJapaneseRune);

    public static bool HasNonJapaneseLookupContent(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsJapaneseRune(rune) || char.IsWhiteSpace((char)rune.Value) || IsJapanesePunctuation(rune))
                continue;

            return true;
        }

        return false;
    }

    public static JapaneseTextKind Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return JapaneseTextKind.Empty;

        var hasKana = false;
        var hasKanji = false;
        var hasJapanese = false;
        var hasNonJapanese = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsKana(rune))
            {
                hasKana = true;
                hasJapanese = true;
            }
            else if (IsKanji(rune))
            {
                hasKanji = true;
                hasJapanese = true;
            }
            else if (IsJapaneseRune(rune))
            {
                hasJapanese = true;
            }
            else if (!char.IsWhiteSpace((char)rune.Value) && !IsJapanesePunctuation(rune))
            {
                hasNonJapanese = true;
            }
        }

        if (!hasJapanese)
            return JapaneseTextKind.NoJapanese;

        if (hasNonJapanese)
            return JapaneseTextKind.MixedJapaneseAndNonJapanese;

        if (hasKana && !hasKanji)
            return JapaneseTextKind.KanaOnly;

        if (hasKanji && !hasKana)
            return JapaneseTextKind.KanjiOnly;

        return JapaneseTextKind.MixedJapanese;
    }

    public static string ExtractJapaneseForLookup(string text)
    {
        var builder = new StringBuilder();
        var previousWasJapanese = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (IsJapaneseRune(rune) || IsJapanesePunctuation(rune))
            {
                builder.Append(rune);
                previousWasJapanese = true;
            }
            else if (char.IsWhiteSpace((char)rune.Value))
            {
                if (previousWasJapanese && builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');

                previousWasJapanese = false;
            }
            else
            {
                previousWasJapanese = false;
            }
        }

        return builder.ToString().Trim();
    }

    public static List<string> ExtractUniqueKanji(string text)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();

        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsKanji(rune))
                continue;

            var value = rune.ToString();
            if (seen.Add(value))
                result.Add(value);
        }

        return result;
    }

    private static bool IsJapaneseRune(Rune rune)
        => IsKana(rune) || IsKanji(rune) || IsJapanesePunctuation(rune);

    private static bool IsKana(Rune rune)
    {
        var value = rune.Value;
        return (value >= 0x3040 && value <= 0x309F) // Hiragana
               || (value >= 0x30A0 && value <= 0x30FF) // Katakana
               || (value >= 0x31F0 && value <= 0x31FF); // Katakana phonetic extensions
    }

    private static bool IsKanji(Rune rune)
    {
        var value = rune.Value;
        return value >= 0x4E00 && value <= 0x9FFF;
    }

    private static bool IsJapanesePunctuation(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x3000 and <= 0x303F
               || value is 'ー' or '・' or '「' or '」' or '『' or '』';
    }
}
