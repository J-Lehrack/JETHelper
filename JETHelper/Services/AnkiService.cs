using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JETHelper.Models;

namespace JETHelper.Services;

/// <summary>
/// Talks to AnkiConnect through its local HTTP API.
/// </summary>
public sealed class AnkiService : IDisposable {
    private readonly HttpClient httpClient = new() {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Dispose() => httpClient.Dispose();

    /// <summary>
    /// Loads available decks, note types, and each note type's fields. The
    /// configuration window uses this information for dropdowns and mappings.
    /// </summary>
    public AnkiConnectionResult TestConnection(Configuration configuration)
    {
        try {
            var decks = InvokeStringListAction(configuration.AnkiConnectUrl,
                                               "deckNames");
            var models = InvokeStringListAction(configuration.AnkiConnectUrl,
                                                "modelNames");
            var modelFields = new Dictionary<string, List<string>>(
                      StringComparer.Ordinal);

            foreach (var model in models) {
                modelFields[model] = InvokeStringListAction(
                          configuration.AnkiConnectUrl,
                          "modelFieldNames",
                          new { modelName = model });
            }

            return new AnkiConnectionResult(
                      Success: true,
                      Message: "Connected to AnkiConnect.",
                      DeckNames: decks,
                      NoteTypeNames: models,
                      ModelFields: modelFields);
        }
        catch (Exception ex) {
            return new AnkiConnectionResult(
                      Success: false,
                      Message: "Could not connect to AnkiConnect: "
                                + ex.Message,
                      DeckNames: [],
                      NoteTypeNames: [],
                      ModelFields: new Dictionary<string, List<string>>(
                                StringComparer.Ordinal));
        }
    }

    public AnkiAddResult AddVocabularyCard(Configuration configuration,
                                           VocabularyCardData vocab)
    {
        var missingValues = new List<string>();
        if (string.IsNullOrWhiteSpace(vocab.Expression))
            missingValues.Add("Expression");
        if (vocab.EnglishDefinitions.Count == 0)
            missingValues.Add("Meaning English");

        if (missingValues.Count > 0)
            return AnkiAddResult.Fail("Cannot create vocabulary card. Missing "
                                      + "required lookup data: "
                                      + string.Join(", ", missingValues) + ".");

        var valuesByRole = new Dictionary<string, string>(
                  StringComparer.Ordinal) {
            ["Expression"] = vocab.Expression,
            ["Furigana"] = BuildFuriganaField(vocab),
            ["Meaning English"] = FormatDefinitions(vocab.EnglishDefinitions),
            ["Meaning Japanese"] = FormatDefinitions(vocab.JapaneseDefinitions),
            ["Meaning Slang"] = FormatDefinitions(vocab.SlangDefinitions),
            ["Audio"] = vocab.Audio,
            ["Frequency"] = vocab.Frequency.HasValue
                                      ? vocab.Frequency.DisplayText
                                      : string.Empty,
            ["Sentence"] = vocab.Sentence,
            ["Pitch Accent"] = vocab.PitchAccent.HasValue
                                         ? vocab.PitchAccent.DisplayText
                                         : string.Empty
        };

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Expression"] = configuration.VocabularyExpressionField,
            ["Furigana"] = configuration.VocabularyFuriganaField,
            ["Meaning English"] = configuration.VocabularyMeaningEnglishField,
            ["Meaning Japanese"] = configuration.VocabularyMeaningJapaneseField,
            ["Meaning Slang"] = configuration.VocabularyMeaningSlangField,
            ["Audio"] = configuration.VocabularyAudioField,
            ["Frequency"] = configuration.VocabularyFrequencyField,
            ["Sentence"] = configuration.VocabularySentenceField,
            ["Pitch Accent"] = configuration.VocabularyPitchAccentField
        };

        return AddMappedNote(
                  configuration.AnkiConnectUrl,
                  configuration.VocabularyDeckName,
                  configuration.VocabularyNoteTypeName,
                  valuesByRole,
                  mappings,
                  requiredRoles: ["Expression", "Meaning English"],
                  tags: BuildTags(vocab.Tags, "vocab"),
                  successMessage: $"Added vocabulary card: {vocab.Expression}");
    }

    public AnkiAddResult AddKanjiCard(Configuration configuration,
                                      KanjiCardData kanji)
    {
        var missingValues = new List<string>();
        if (string.IsNullOrWhiteSpace(kanji.KanjiCharacter))
            missingValues.Add("Kanji Character");
        if (kanji.Meanings.Count == 0)
            missingValues.Add("Meaning");

        if (missingValues.Count > 0)
            return AnkiAddResult.Fail(
                      "Cannot create kanji card. Missing required lookup data: "
                      + string.Join(", ", missingValues) + ".");

        var valuesByRole = new Dictionary<string, string>(
                  StringComparer.Ordinal) {
            ["Kanji Character"] = kanji.KanjiCharacter,
            ["Meaning"] = NumberedLines(kanji.Meanings),
            ["Kunyomi"] = string.Join("、", kanji.Kunyomi),
            ["Onyomi"] = string.Join("、", kanji.Onyomi),
            ["Frequency"] = kanji.Frequency.HasValue
                                      ? kanji.Frequency.DisplayText
                                      : string.Empty,
            ["Sentence"] = kanji.Sentence,
            [
                "Strokes"
            ] = kanji.StrokeCount is null
                          ? string.Empty
                          : $"Stroke count: {kanji.StrokeCount.Value}",
            ["Diagram"] = kanji.Diagram
        };

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["Kanji Character"] = configuration.KanjiCharacterField,
            ["Meaning"] = configuration.KanjiMeaningField,
            ["Kunyomi"] = configuration.KanjiKunyomiField,
            ["Onyomi"] = configuration.KanjiOnyomiField,
            ["Frequency"] = configuration.KanjiFrequencyField,
            ["Sentence"] = configuration.KanjiSentenceField,
            ["Strokes"] = configuration.KanjiStrokesField,
            ["Diagram"] = configuration.KanjiDiagramField
        };

        return AddMappedNote(
                  configuration.AnkiConnectUrl,
                  configuration.KanjiDeckName,
                  configuration.KanjiNoteTypeName,
                  valuesByRole,
                  mappings,
                  requiredRoles: ["Kanji Character", "Meaning"],
                  tags: BuildTags(kanji.Tags, "kanji"),
                  successMessage: $"Added kanji card: {kanji.KanjiCharacter}");
    }

    private AnkiAddResult
    AddMappedNote(string url,
                  string deckName,
                  string modelName,
                  IReadOnlyDictionary<string, string> valuesByRole,
                  IReadOnlyDictionary<string, string> mappings,
                  IReadOnlyCollection<string> requiredRoles,
                  List<string> tags,
                  string successMessage)
    {
        if (string.IsNullOrWhiteSpace(deckName))
            return AnkiAddResult.Fail(
                      "No Anki deck is selected. Choose one in /jetconfig.");

        if (string.IsNullOrWhiteSpace(modelName))
            return AnkiAddResult.Fail("No Anki note type is selected. Choose "
                                      + "one in /jetconfig.");

        try {
            var modelFields = InvokeStringListAction(url,
                                                     "modelFieldNames",
                                                     new { modelName });

            var unmappedRequiredRoles
                      = requiredRoles
                                  .Where(role => !mappings.TryGetValue(
                                                           role, out var field)
                                                 || string.IsNullOrWhiteSpace(
                                                           field))
                                  .ToList();

            if (unmappedRequiredRoles.Count > 0)
                return AnkiAddResult.Fail(
                          "Required Anki field mappings are not configured: "
                          + string.Join(", ", unmappedRequiredRoles)
                          + ". Configure them in /jetcardconfig.");

            var invalidRequiredMappings
                      = requiredRoles
                                  .Where(role => !modelFields.Contains(
                                                   mappings[role],
                                                   StringComparer.Ordinal))
                                  .Select(role => $"{role} → {mappings[role]}")
                                  .ToList();

            if (invalidRequiredMappings.Count > 0)
                return AnkiAddResult.Fail(
                          "The selected note type no longer contains required "
                          + "mapped fields: "
                          + string.Join(", ", invalidRequiredMappings)
                          + ". Update the mappings in /jetcardconfig.");

            var activeMappings
                      = mappings.Where(pair => !string.IsNullOrWhiteSpace(
                                                         pair.Value)
                                               && modelFields.Contains(
                                                         pair.Value,
                                                         StringComparer
                                                                   .Ordinal))
                                  .ToList();

            var duplicateTargets = activeMappings
                                             .GroupBy(pair => pair.Value,
                                                      StringComparer.Ordinal)
                                             .Where(group => group.Count() > 1)
                                             .Select(group => group.Key)
                                             .ToList();

            if (duplicateTargets.Count > 0)
                return AnkiAddResult.Fail("Multiple JETHelper data fields are "
                                          + "mapped to the same Anki field: "
                                          + string.Join(", ", duplicateTargets)
                                          + (". Give each mapping a unique "
                                             + "target in /jetcardconfig."));

            var fields = activeMappings.ToDictionary(
                      pair => pair.Value,
                      pair => valuesByRole.TryGetValue(pair.Key, out var value)
                                        ? value
                                        : string.Empty,
                      StringComparer.Ordinal);

            var note = new { deckName,
                             modelName,
                             fields,
                             options = new { allowDuplicate = false,
                                             duplicateScope = "deck" },
                             tags };

            var result = InvokeAction(url, "addNote", new { note });
            return AnkiAddResult.Ok(successMessage, result.ToString());
        }
        catch (Exception ex) {
            return AnkiAddResult.Fail("Could not add card to Anki: "
                                      + ex.Message);
        }
    }

    private List<string> InvokeStringListAction(
              string url,
              string action) => InvokeStringListAction(url, action, new { });

    private List<string>
    InvokeStringListAction(string url, string action, object parameters)
    {
        var result = InvokeAction(url, action, parameters);
        if (result.ValueKind != JsonValueKind.Array)
            return [];

        return result.EnumerateArray()
                  .Select(x => x.GetString() ?? string.Empty)
                  .Where(x => !string.IsNullOrWhiteSpace(x))
                  .ToList();
    }

    private JsonElement
    InvokeAction(string url, string action, object parameters)
    {
        var payload = JsonSerializer.Serialize(new {
            action, version = 6, @params = parameters
        }, JsonOptions);

        using var content = new StringContent(payload,
                                              Encoding.UTF8,
                                              "application/json");
        using var response
                  = httpClient.PostAsync(url, content).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var json = response.Content.ReadAsStringAsync()
                             .GetAwaiter()
                             .GetResult();
        var parsed = JsonSerializer.Deserialize<AnkiConnectResponse>(
                               json, JsonOptions)
                     ?? throw new InvalidOperationException(
                               "AnkiConnect returned an empty response.");

        if (!string.IsNullOrWhiteSpace(parsed.Error))
            throw new InvalidOperationException(parsed.Error);

        return parsed.Result;
    }

    private static string BuildFuriganaField(VocabularyCardData vocab)
    {
        if (string.IsNullOrWhiteSpace(vocab.Furigana))
            return vocab.Expression;

        return FuriganaFormatter.Format(vocab.Expression, vocab.Furigana);
    }

    private static string
    FormatDefinitions(IReadOnlyCollection<DictionaryDefinition> definitions)
    {
        if (definitions.Count == 0)
            return string.Empty;

        var items = definitions.Take(8).Select(
                  definition =>
                  {
                      var source
                                = string.IsNullOrWhiteSpace(
                                            definition.SourceDisplay)
                                            ? string.Empty
                                            : $" <span class=\"jet-source\">({Html(definition.SourceDisplay)})</span>";
                      var partOfSpeech
                                = string.IsNullOrWhiteSpace(
                                            definition.PartOfSpeechDisplay)
                                            ? string.Empty
                                            : $" <span class=\"jet-pos\">{Html(definition.PartOfSpeechDisplay)}</span>";
                      return $"<li>{Html(definition.Meaning)}{partOfSpeech}{source}</li>";
                  });

        return "<ul>" + string.Join(string.Empty, items) + "</ul>";
    }

    private static string NumberedLines(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return string.Empty;

        return string.Join("<br>",
                           values.Select(
                                     (value,
                                      index) => $"{index + 1}. {Html(value)}"));
    }

    private static List<string> BuildTags(IEnumerable<string> sourceTags,
                                          string cardType)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "jethelper", "ffxiv", cardType
        };

        foreach (var tag in sourceTags) {
            var cleaned = tag.Trim().Replace(' ', '_').Replace('/', '_');
            if (!string.IsNullOrWhiteSpace(cleaned))
                tags.Add(cleaned);
        }

        return tags.ToList();
    }

    private static string
    Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private sealed class AnkiConnectResponse {
        public JsonElement Result { get; set; }
        public string? Error { get; set; }
    }
}

public sealed record
AnkiConnectionResult(bool Success,
                     string Message,
                     List<string> DeckNames,
                     List<string> NoteTypeNames,
                     Dictionary<string, List<string>> ModelFields);

public sealed record AnkiAddResult(bool Success, string Message, string? NoteId)
{
    public static AnkiAddResult Ok(string message, string? noteId)
        => new(true, message, noteId);

    public static AnkiAddResult Fail(
              string message) => new(false, message, null);
}
