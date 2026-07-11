using System;
using System.Collections.Generic;
using System.Linq;

namespace JETHelper.Services;

/// <summary>
/// Generates simple dictionary-form candidates from common Japanese inflections.
///
/// This is intentionally a small, practical first pass rather than a complete
/// Japanese tokenizer. It handles common forms we expect players to copy from
/// chat/cutscene text, then the loaded term dictionaries decide whether the
/// generated base form is a real dictionary entry.
/// </summary>
public sealed class DeinflectionService
{
    public IReadOnlyList<DeinflectionCandidate> Generate(string surfaceText)
    {
        if (string.IsNullOrWhiteSpace(surfaceText))
            return [];

        var results = new List<DeinflectionCandidate>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        void Add(string text, string reason)
        {
            if (string.IsNullOrWhiteSpace(text) || text == surfaceText)
                return;

            if (seen.Add(text))
                results.Add(new DeinflectionCandidate(text, reason));
        }

        // Polite ichidan-style verb forms. These catch high-value cases like:
        // 食べます / 食べました / 食べません / 食べませんでした -> 食べる.
        ReplaceSuffix(surfaceText, "ませんでした", "る", "polite negative past verb", Add);
        ReplaceSuffix(surfaceText, "ません", "る", "polite negative verb", Add);
        ReplaceSuffix(surfaceText, "ました", "る", "polite past verb", Add);
        ReplaceSuffix(surfaceText, "ます", "る", "polite verb", Add);

        // Ichidan-style short forms.
        ReplaceSuffix(surfaceText, "なかった", "る", "negative past ichidan verb", Add);
        ReplaceSuffix(surfaceText, "ない", "る", "negative ichidan verb", Add);
        ReplaceSuffix(surfaceText, "た", "る", "past/た-form ichidan verb", Add);
        ReplaceSuffix(surfaceText, "て", "る", "て-form ichidan verb", Add);

        // I-adjectives: 高くて -> 高い, よかった -> よい.
        ReplaceSuffix(surfaceText, "くなかった", "い", "negative past i-adjective", Add);
        ReplaceSuffix(surfaceText, "くない", "い", "negative i-adjective", Add);
        ReplaceSuffix(surfaceText, "かった", "い", "past i-adjective", Add);
        ReplaceSuffix(surfaceText, "くて", "い", "て-form i-adjective", Add);
        ReplaceSuffix(surfaceText, "く", "い", "adverbial i-adjective", Add);

        // Common irregular adjective spelling. Many term dictionaries contain both いい and よい,
        // but よかった usually maps more cleanly to よい.
        if (surfaceText is "よかった" or "良かった")
            Add(surfaceText[0] == '良' ? "良い" : "よい", "irregular i-adjective");

        // Na-adjective/noun copula helpers: 綺麗な -> 綺麗, 静かだった -> 静か.
        ReplaceSuffix(surfaceText, "だった", string.Empty, "past copula / na-adjective", Add);
        ReplaceSuffix(surfaceText, "でした", string.Empty, "polite copula / na-adjective", Add);
        ReplaceSuffix(surfaceText, "ではない", string.Empty, "negative copula / na-adjective", Add);
        ReplaceSuffix(surfaceText, "じゃない", string.Empty, "negative copula / na-adjective", Add);
        ReplaceSuffix(surfaceText, "な", string.Empty, "na-adjective modifier", Add);

        // Godan negative forms: 書かない -> 書く, 読まない -> 読む, 買わない -> 買う.
        ReplaceGodanNegative(surfaceText, Add);

        // Godan て/た forms. These are ambiguous, but dictionary validation filters
        // out most bad guesses. Example: 読んだ -> 読む, 書いた -> 書く.
        ReplaceGodanTeTa(surfaceText, Add);

        return results;
    }

    private static void ReplaceSuffix(string text,
                                      string suffix,
                                      string replacement,
                                      string reason,
                                      Action<string, string> add)
    {
        if (text.Length <= suffix.Length || !text.EndsWith(suffix, System.StringComparison.Ordinal))
            return;

        var stem = text[..^suffix.Length];
        add(stem + replacement, reason);
    }

    private static void ReplaceGodanNegative(string text, Action<string, string> add)
    {
        var rules = new (string Suffix, string Replacement)[]
        {
            ("わない", "う"),
            ("かない", "く"),
            ("がない", "ぐ"),
            ("さない", "す"),
            ("たない", "つ"),
            ("なない", "ぬ"),
            ("ばない", "ぶ"),
            ("まない", "む"),
            ("らない", "る"),
        };

        foreach (var (suffix, replacement) in rules)
            ReplaceSuffix(text, suffix, replacement, "negative godan verb", add);
    }

    private static void ReplaceGodanTeTa(string text, Action<string, string> add)
    {
        var rules = new (string Suffix, string Replacement)[]
        {
            ("った", "う"),
            ("って", "う"),
            ("いた", "く"),
            ("いて", "く"),
            ("いだ", "ぐ"),
            ("いで", "ぐ"),
            ("した", "す"),
            ("して", "す"),
            ("んだ", "む"),
            ("んで", "む"),
            ("んだ", "ぶ"),
            ("んで", "ぶ"),
            ("んだ", "ぬ"),
            ("んで", "ぬ"),
        };

        foreach (var (suffix, replacement) in rules)
            ReplaceSuffix(text, suffix, replacement, "て/た-form godan verb", add);
    }
}

public readonly record struct DeinflectionCandidate(string Text, string Reason);
