using System;
using System.Collections.Generic;
using System.Text;

namespace JETHelper.Lookup.Services;

/// <summary>
/// Converts an expression and its dictionary reading into Anki's bracket-style
/// furigana notation.
///
/// Examples:
/// - 食べる + たべる => 食[た]べる
/// - 申し込む + もうしこむ => 申[もう]し込[こ]む
/// - 漢字 + かんじ => 漢字[かんじ]
/// - カタカナ + かたかな => カタカナ
///
/// This is intentionally conservative. Kana already visible in the expression
/// is used as an alignment anchor. Reading text between those anchors is attached
/// to the preceding kanji-containing run. If alignment is unsafe, the formatter
/// falls back to annotating the whole expression rather than inventing a split.
/// </summary>
public static class FuriganaFormatter
{
    public static string Format(string expression, string reading)
    {
        expression = expression?.Trim() ?? string.Empty;
        reading = reading?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(reading) || ContainsOnlyKana(expression))
            return expression;

        var runs = SplitIntoRuns(expression);
        if (runs.Count == 1)
            return runs[0].IsKana
                       ? expression
                       : $"{expression}[{reading}]";

        var normalizedReading = NormalizeKana(reading);
        var readingPosition = 0;
        var output = new StringBuilder();

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];

            if (run.IsKana)
            {
                var normalizedAnchor = NormalizeKana(run.Text);

                // A leading kana run must match the beginning of the reading.
                // There is no preceding kanji run to receive extra characters.
                if (index == 0)
                {
                    if (!StartsWithAt(normalizedReading,
                                      normalizedAnchor,
                                      readingPosition))
                        return WholeExpressionFallback(expression, reading);

                    output.Append(run.Text);
                    readingPosition += normalizedAnchor.Length;
                    continue;
                }

                var anchorIndex = normalizedReading.IndexOf(
                          normalizedAnchor,
                          readingPosition,
                          StringComparison.Ordinal);

                if (anchorIndex < readingPosition)
                    return WholeExpressionFallback(expression, reading);

                var readingForPreviousKanji = reading.Substring(
                          readingPosition,
                          anchorIndex - readingPosition);

                if (string.IsNullOrWhiteSpace(readingForPreviousKanji))
                    return WholeExpressionFallback(expression, reading);

                output.Append('[')
                      .Append(readingForPreviousKanji)
                      .Append(']')
                      .Append(run.Text);

                readingPosition = anchorIndex + normalizedAnchor.Length;
                continue;
            }

            // Anki's furigana filter looks backward from [reading] until it
            // reaches whitespace or another boundary. Without this separator,
            // 食[た]べ物[もの] displays もの over both べ and 物. A space before
            // a later kanji run creates the desired 食[た]べ 物[もの] output;
            // Anki's rendered card keeps the visual gap minimal.
            if (output.Length > 0 && !char.IsWhiteSpace(output[^1]))
                output.Append(' ');

            output.Append(run.Text);

            // If the expression ends in kanji, all remaining reading belongs to
            // that final kanji-containing run.
            if (index == runs.Count - 1)
            {
                var remainder = reading[readingPosition..];
                if (string.IsNullOrWhiteSpace(remainder))
                    return WholeExpressionFallback(expression, reading);

                output.Append('[').Append(remainder).Append(']');
                readingPosition = reading.Length;
            }
        }

        return readingPosition == reading.Length
                   ? output.ToString()
                   : WholeExpressionFallback(expression, reading);
    }

    private static string WholeExpressionFallback(string expression,
                                                  string reading)
        => $"{expression}[{reading}]";

    private static bool StartsWithAt(string source,
                                     string value,
                                     int startIndex)
    {
        if (startIndex < 0 || startIndex + value.Length > source.Length)
            return false;

        return source.AsSpan(startIndex, value.Length)
                     .SequenceEqual(value.AsSpan());
    }

    private static List<TextRun> SplitIntoRuns(string text)
    {
        var runs = new List<TextRun>();
        if (string.IsNullOrEmpty(text))
            return runs;

        var currentIsKana = IsKana(text[0]);
        var current = new StringBuilder();

        foreach (var character in text)
        {
            var isKana = IsKana(character);
            if (current.Length > 0 && isKana != currentIsKana)
            {
                runs.Add(new TextRun(current.ToString(), currentIsKana));
                current.Clear();
                currentIsKana = isKana;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            runs.Add(new TextRun(current.ToString(), currentIsKana));

        return runs;
    }

    private static bool ContainsOnlyKana(string text)
    {
        foreach (var character in text)
        {
            if (!IsKana(character))
                return false;
        }

        return text.Length > 0;
    }

    private static bool IsKana(char character)
        => character is >= '\u3040' and <= '\u309F'
           or >= '\u30A0' and <= '\u30FF'
           or '\u30FC';

    /// <summary>
    /// Katakana and hiragana represent the same sounds but use different code
    /// points. Normalizing katakana to hiragana lets カタ and かた act as the
    /// same alignment anchor.
    /// </summary>
    private static string NormalizeKana(string value)
    {
        var output = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is >= '\u30A1' and <= '\u30F6')
                output.Append((char)(character - 0x60));
            else
                output.Append(character);
        }

        return output.ToString();
    }

    private sealed record TextRun(string Text, bool IsKana);
}
