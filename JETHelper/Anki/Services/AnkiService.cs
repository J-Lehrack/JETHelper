using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JETHelper.Anki.Models;
using JETHelper.Anki.Templates;
using JETHelper.Diagnostics.Services;
using JETHelper.Dictionaries.Models;
using JETHelper.Lookup.Services;

namespace JETHelper.Anki.Services;

/// <summary>
/// Talks to AnkiConnect through its local HTTP API.
///
/// All network operations are asynchronous so a slow or unavailable Anki
/// instance cannot block Dalamud's framework/UI thread.
/// </summary>
public sealed class AnkiService : IDisposable {
    private readonly DiagnosticService diagnostics;
    private readonly object lifecycleLock = new();
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly HttpClient httpClient = new() {
        Timeout = TimeSpan.FromSeconds(4)
    };
    private bool disposed;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AnkiService(DiagnosticService diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            if (disposed)
                return;

            disposed = true;
            disposeCancellation.Cancel();
            disposeCancellation.Dispose();
        }

        httpClient.Dispose();
    }

    /// <summary>
    /// Loads available decks, note types, and each note type's fields. The
    /// configuration window uses this information for dropdowns and mappings.
    /// </summary>
    public async Task<AnkiConnectionResult>
    TestConnectionAsync(Configuration configuration,
                        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CreateOperationCancellation(
                  cancellationToken);
        var token = linkedCancellation.Token;
        var url = configuration.AnkiConnectUrl;
        var stopwatch = Stopwatch.StartNew();

        try {
            var decks = await InvokeStringListActionAsync(url,
                                                          "deckNames",
                                                          token)
                                  .ConfigureAwait(false);
            var models = await InvokeStringListActionAsync(url,
                                                           "modelNames",
                                                           token)
                                   .ConfigureAwait(false);
            var modelFields = new Dictionary<string, List<string>>(
                      StringComparer.Ordinal);

            // Keep requests sequential. AnkiConnect is a local single-user API,
            // and flooding it with one request per note type would add needless
            // peak work for users with large Anki collections.
            foreach (var model in models) {
                token.ThrowIfCancellationRequested();
                modelFields[model] = await InvokeStringListActionAsync(
                                               url,
                                               "modelFieldNames",
                                               new { modelName = model },
                                               token)
                                               .ConfigureAwait(false);
            }

            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Connection refresh succeeded in "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"Decks={decks.Count}; note types={models.Count}.");

            return new AnkiConnectionResult(
                      Success: true,
                      Message: "Connected to AnkiConnect.",
                      DeckNames: decks,
                      NoteTypeNames: models,
                      ModelFields: modelFields);
        }
        catch (OperationCanceledException)
                  when (token.IsCancellationRequested) {
            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Connection refresh cancelled after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms.");

            return FailedConnectionResult("AnkiConnect refresh was cancelled.");
        }
        catch (Exception ex) {
            stopwatch.Stop();
            diagnostics.Error(
                      "AnkiConnect",
                      $"Connection refresh failed after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms.",
                      ex);

            return FailedConnectionResult("Could not connect to AnkiConnect: "
                                          + ex.Message);
        }
    }

    public async Task<AnkiAddResult>
    AddVocabularyCardAsync(Configuration configuration,
                           VocabularyCardData vocab,
                           CancellationToken cancellationToken = default)
    {
        var missingValues = new List<string>();
        if (string.IsNullOrWhiteSpace(vocab.Expression))
            missingValues.Add("Expression");
        if (vocab.EnglishDefinitions.Count == 0)
            missingValues.Add("Meaning English");

        if (missingValues.Count > 0)
            return FailWithDiagnostic("Cannot create vocabulary card. Missing "
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

        return await AddMappedNoteAsync(
                         configuration.AnkiConnectUrl,
                         configuration.VocabularyDeckName,
                         configuration.VocabularyNoteTypeName,
                         valuesByRole,
                         mappings,
                         requiredRoles: ["Expression", "Meaning English"],
                         tags: BuildTags(vocab.Tags, "vocab"),
                         successMessage: $"Added vocabulary card: {vocab.Expression}",
                         cancellationToken: cancellationToken)
                  .ConfigureAwait(false);
    }

    public async Task<AnkiAddResult>
    AddKanjiCardAsync(Configuration configuration,
                      KanjiCardData kanji,
                      CancellationToken cancellationToken = default)
    {
        var missingValues = new List<string>();
        if (string.IsNullOrWhiteSpace(kanji.KanjiCharacter))
            missingValues.Add("Kanji Character");
        if (kanji.Meanings.Count == 0)
            missingValues.Add("Meaning");

        if (missingValues.Count > 0)
            return FailWithDiagnostic(
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

        return await AddMappedNoteAsync(
                         configuration.AnkiConnectUrl,
                         configuration.KanjiDeckName,
                         configuration.KanjiNoteTypeName,
                         valuesByRole,
                         mappings,
                         requiredRoles: ["Kanji Character", "Meaning"],
                         tags: BuildTags(kanji.Tags, "kanji"),
                         successMessage: $"Added kanji card: {kanji.KanjiCharacter}",
                         cancellationToken: cancellationToken)
                  .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates or confirms both optional recommended JETHelper decks. Anki's
    /// createDeck action is idempotent, so existing decks are left intact.
    /// This operation is intentionally independent from note-type installation.
    /// </summary>
    public async Task<AnkiDeckCreationResult>
    CreateRecommendedJetHelperDecksAsync(
              Configuration configuration,
              CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CreateOperationCancellation(
                  cancellationToken);
        var token = linkedCancellation.Token;
        var url = configuration.AnkiConnectUrl;
        var stopwatch = Stopwatch.StartNew();
        var readyDecks = new List<string>();

        try {
            foreach (var deckName in new[] {
                         JETHelperAnkiTemplates.VocabularyDeckName,
                         JETHelperAnkiTemplates.KanjiDeckName
                     }) {
                token.ThrowIfCancellationRequested();
                await InvokeActionAsync(
                          url, "createDeck", new { deck = deckName }, token)
                          .ConfigureAwait(false);
                readyDecks.Add(deckName);
            }

            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Created or confirmed recommended decks in "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"decks={string.Join(", ", readyDecks)}.");

            return AnkiDeckCreationResult.Ok(
                      "Created or confirmed both recommended JETHelper decks.",
                      readyDecks);
        }
        catch (OperationCanceledException)
                  when (token.IsCancellationRequested) {
            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Recommended deck creation cancelled after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"ready decks={string.Join(", ", readyDecks)}.");

            return AnkiDeckCreationResult.Failed(
                      "Recommended deck creation was cancelled.", readyDecks);
        }
        catch (Exception ex) {
            stopwatch.Stop();
            diagnostics.Error(
                      "AnkiConnect",
                      $"Recommended deck creation failed after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"ready decks={string.Join(", ", readyDecks)}.",
                      ex);

            var partialMessage = readyDecks.Count == 0
                                           ? string.Empty
                                           : " These decks were ready before "
                                                       + "the failure: "
                                                       + string.Join(", ",
                                                                     readyDecks)
                                                       + ".";

            return AnkiDeckCreationResult.Failed(
                      "Could not create or confirm both recommended decks: "
                                + ex.Message + partialMessage,
                      readyDecks);
        }
    }

    /// <summary>
    /// Creates the optional JETHelper vocabulary note type. Existing note types
    /// are never overwritten; a compatible existing type is left unchanged and
    /// may be selected for use.
    /// </summary>
    public Task<AnkiTemplateInstallResult>
    InstallJetHelperVocabularyNoteTypeAsync(
              Configuration configuration,
              CancellationToken cancellationToken
              = default) => InstallTemplateBundleAsync(configuration
                                                                 .AnkiConnectUrl,
                                                       JETHelperAnkiTemplates
                                                                 .Vocabulary,
                                                       cancellationToken);

    /// <summary>
    /// Creates the optional JETHelper kanji note type. Existing note types are
    /// never overwritten.
    /// </summary>
    public Task<AnkiTemplateInstallResult> InstallJetHelperKanjiNoteTypeAsync(
              Configuration configuration,
              CancellationToken cancellationToken
              = default) => InstallTemplateBundleAsync(configuration
                                                                 .AnkiConnectUrl,
                                                       JETHelperAnkiTemplates
                                                                 .Kanji,
                                                       cancellationToken);

    private async Task<AnkiTemplateInstallResult>
    InstallTemplateBundleAsync(string url,
                               AnkiTemplateBundle bundle,
                               CancellationToken cancellationToken)
    {
        using var linkedCancellation = CreateOperationCancellation(
                  cancellationToken);
        var token = linkedCancellation.Token;
        var stopwatch = Stopwatch.StartNew();

        try {
            var modelNames = await InvokeStringListActionAsync(url,
                                                               "modelNames",
                                                               token)
                                       .ConfigureAwait(false);
            if (modelNames.Contains(bundle.NoteTypeName,
                                    StringComparer.Ordinal)) {
                var existingFields
                          = await InvokeStringListActionAsync(
                                      url,
                                      "modelFieldNames",
                                      new { modelName = bundle.NoteTypeName },
                                      token)
                                      .ConfigureAwait(false);

                var missingFields
                          = bundle.Fields
                                      .Where(field => !existingFields.Contains(
                                                       field,
                                                       StringComparer.Ordinal))
                                      .ToList();

                var existingTemplates
                          = missingFields.Count == 0
                                      ? await InvokeActionAsync(
                                                  url,
                                                  "modelTemplates",
                                                  new { modelName
                                                        = bundle.NoteTypeName },
                                                  token)
                                                  .ConfigureAwait(false)
                                      : default;
                var existingStyling
                          = missingFields.Count == 0
                                      ? await InvokeActionAsync(
                                                  url,
                                                  "modelStyling",
                                                  new { modelName
                                                        = bundle.NoteTypeName },
                                                  token)
                                                  .ConfigureAwait(false)
                                      : default;
                var isRecognizedJetHelperType
                          = missingFields.Count == 0
                            && IsRecognizedJetHelperNoteType(bundle,
                                                             existingTemplates,
                                                             existingStyling);

                stopwatch.Stop();

                if (missingFields.Count > 0) {
                    var incompatibleMessage
                              = $"An Anki note type named \"{bundle.NoteTypeName}\" "
                                + "already exists, but it is missing JETHelper "
                                + "fields: " + string.Join(", ", missingFields)
                                + ". JETHelper left it unchanged.";

                    diagnostics.Warning(
                              "AnkiConnect",
                              $"Optional note-type installation rejected after "
                                        + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                        + incompatibleMessage);

                    return AnkiTemplateInstallResult.Failed(
                              incompatibleMessage, bundle.NoteTypeName);
                }

                if (!isRecognizedJetHelperType) {
                    var unrecognizedMessage
                              = $"An Anki note type named \"{bundle.NoteTypeName}\" "
                                + "already exists and has compatible fields, "
                                + "but its "
                                + "templates are not recognized as JETHelper "
                                + "templates. "
                                + "JETHelper left it unchanged. Rename the "
                                + "existing "
                                + "note type before installing, or select it "
                                + "manually.";

                    diagnostics.Warning(
                              "AnkiConnect",
                              $"Optional note-type installation stopped after "
                                        + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                        + unrecognizedMessage);

                    return AnkiTemplateInstallResult.Failed(
                              unrecognizedMessage, bundle.NoteTypeName);
                }

                var existingMessage
                          = $"The JETHelper note type \"{bundle.NoteTypeName}\" "
                            + "already exists. Its templates and styling were "
                            + "left " + "unchanged.";

                diagnostics.Information(
                          "AnkiConnect",
                          $"Optional JETHelper note type already exists; no overwrite "
                                    + $"performed after "
                                    + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                    + $"note type={bundle.NoteTypeName}.");

                return AnkiTemplateInstallResult.Existing(existingMessage,
                                                          bundle.NoteTypeName);
            }

            var cardTemplates = new[] { new { Name = bundle.CardTemplateName,
                                              Front = bundle.FrontTemplate,
                                              Back = bundle.BackTemplate } };

            await InvokeActionAsync(url,
                                    "createModel",
                                    new { modelName = bundle.NoteTypeName,
                                          inOrderFields = bundle.Fields,
                                          css = bundle.Css,
                                          isCloze = false,
                                          cardTemplates },
                                    token)
                      .ConfigureAwait(false);

            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Created optional note type in "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"note type={bundle.NoteTypeName}; "
                                + $"template version={JETHelperAnkiTemplates.TemplateVersion}.");
            return AnkiTemplateInstallResult.Created(
                      $"Created the note type \"{bundle.NoteTypeName}\".",
                      bundle.NoteTypeName);
        }
        catch (OperationCanceledException)
                  when (token.IsCancellationRequested) {
            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Optional note-type installation cancelled after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"note type={bundle.NoteTypeName}.");

            return AnkiTemplateInstallResult.Failed(
                      "JETHelper note-type installation was cancelled.",
                      bundle.NoteTypeName);
        }
        catch (Exception ex) {
            stopwatch.Stop();
            diagnostics.Error(
                      "AnkiConnect",
                      $"Optional note-type installation failed after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms. "
                                + $"note type={bundle.NoteTypeName}.",
                      ex);

            return AnkiTemplateInstallResult.Failed(
                      "Could not install the JETHelper note type: "
                                + ex.Message,
                      bundle.NoteTypeName);
        }
    }

    private async Task<AnkiAddResult>
    AddMappedNoteAsync(string url,
                       string deckName,
                       string modelName,
                       IReadOnlyDictionary<string, string> valuesByRole,
                       IReadOnlyDictionary<string, string> mappings,
                       IReadOnlyCollection<string> requiredRoles,
                       List<string> tags,
                       string successMessage,
                       CancellationToken cancellationToken)
    {
        using var linkedCancellation = CreateOperationCancellation(
                  cancellationToken);
        var token = linkedCancellation.Token;
        var stopwatch = Stopwatch.StartNew();
        var context = $"deck={deckName}; note type={modelName}";

        if (string.IsNullOrWhiteSpace(deckName)) {
            stopwatch.Stop();
            return FailWithDiagnostic(
                      "No Anki deck is selected. Choose one in /jetconfig.",
                      $"Add note rejected after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}.");
        }

        if (string.IsNullOrWhiteSpace(modelName)) {
            stopwatch.Stop();
            return FailWithDiagnostic(
                      "No Anki note type is selected. Choose one in "
                                + "/jetconfig.",
                      $"Add note rejected after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}.");
        }

        try {
            var modelFields = await InvokeStringListActionAsync(
                                        url,
                                        "modelFieldNames",
                                        new { modelName },
                                        token)
                                        .ConfigureAwait(false);

            var unmappedRequiredRoles
                      = requiredRoles
                                  .Where(role => !mappings.TryGetValue(
                                                           role, out var field)
                                                 || string.IsNullOrWhiteSpace(
                                                           field))
                                  .ToList();

            if (unmappedRequiredRoles.Count > 0) {
                stopwatch.Stop();
                var message
                          = "Required Anki field mappings are not configured: "
                            + string.Join(", ", unmappedRequiredRoles)
                            + ". Configure them in /jetcardconfig.";

                return FailWithDiagnostic(
                          message,
                          $"Add note rejected after "
                                    + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}. "
                                    + message);
            }

            var invalidRequiredMappings
                      = requiredRoles
                                  .Where(role => !modelFields.Contains(
                                                   mappings[role],
                                                   StringComparer.Ordinal))
                                  .Select(role => $"{role} → {mappings[role]}")
                                  .ToList();

            if (invalidRequiredMappings.Count > 0) {
                stopwatch.Stop();
                var message = "The selected note type no longer contains "
                              + "required " + "mapped fields: "
                              + string.Join(", ", invalidRequiredMappings)
                              + ". Update the mappings in /jetcardconfig.";

                return FailWithDiagnostic(
                          message,
                          $"Add note rejected after "
                                    + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}. "
                                    + message);
            }

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

            if (duplicateTargets.Count > 0) {
                stopwatch.Stop();
                var message = "Multiple JETHelper data fields are mapped to "
                              + "the same " + "Anki field: "
                              + string.Join(", ", duplicateTargets)
                              + ". Give each mapping a unique target in "
                              + "/jetcardconfig.";

                return FailWithDiagnostic(
                          message,
                          $"Add note rejected after "
                                    + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}. "
                                    + message);
            }

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

            var result = await InvokeActionAsync(
                                   url, "addNote", new { note }, token)
                                   .ConfigureAwait(false);
            stopwatch.Stop();

            diagnostics.Information(
                      "AnkiConnect",
                      $"Add note succeeded in "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}.");

            return AnkiAddResult.Ok(successMessage, result.ToString());
        }
        catch (OperationCanceledException)
                  when (token.IsCancellationRequested) {
            stopwatch.Stop();
            diagnostics.Information(
                      "AnkiConnect",
                      $"Add note cancelled after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}.");

            return AnkiAddResult.Fail("Anki card creation was cancelled.");
        }
        catch (Exception ex) {
            stopwatch.Stop();
            diagnostics.Error(
                      "AnkiConnect",
                      $"Add note failed after "
                                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms; {context}.",
                      ex);

            return AnkiAddResult.Fail("Could not add card to Anki: "
                                      + ex.Message);
        }
    }

    private AnkiAddResult FailWithDiagnostic(string message,
                                             string? diagnosticMessage = null)
    {
        diagnostics.Warning("AnkiConnect", diagnosticMessage ?? message);
        return AnkiAddResult.Fail(message);
    }

    private Task<List<string>> InvokeStringListActionAsync(
              string url,
              string action,
              CancellationToken
                        cancellationToken) => InvokeStringListActionAsync(url,
                                                                          action,
                                                                          new { },
                                                                          cancellationToken);

    private async Task<List<string>>
    InvokeStringListActionAsync(string url,
                                string action,
                                object parameters,
                                CancellationToken cancellationToken)
    {
        var result = await InvokeActionAsync(
                               url, action, parameters, cancellationToken)
                               .ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Array)
            return [];

        return result.EnumerateArray()
                  .Select(x => x.GetString() ?? string.Empty)
                  .Where(x => !string.IsNullOrWhiteSpace(x))
                  .ToList();
    }

    private async Task<JsonElement>
    InvokeActionAsync(string url,
                      string action,
                      object parameters,
                      CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new {
            action, version = 6, @params = parameters
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(
                      payload, Encoding.UTF8, "application/json")
        };

        try {
            using var response
                      = await httpClient
                                  .SendAsync(request,
                                             HttpCompletionOption
                                                       .ResponseHeadersRead,
                                             cancellationToken)
                                  .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content
                                 .ReadAsStringAsync(cancellationToken)
                                 .ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<AnkiConnectResponse>(
                                   json, JsonOptions)
                         ?? throw new InvalidOperationException(
                                   "AnkiConnect returned an empty response.");

            if (!string.IsNullOrWhiteSpace(parsed.Error))
                throw new InvalidOperationException(parsed.Error);

            return parsed.Result;
        }
        catch (OperationCanceledException ex)
                  when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                      $"AnkiConnect did not respond within "
                                + $"{httpClient.Timeout.TotalSeconds:0} seconds.",
                      ex);
        }
    }

    private CancellationTokenSource
    CreateOperationCancellation(CancellationToken cancellationToken)
    {
        lock (lifecycleLock)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AnkiService));

            return CancellationTokenSource.CreateLinkedTokenSource(
                      cancellationToken, disposeCancellation.Token);
        }
    }

    private static AnkiConnectionResult
    FailedConnectionResult(string message) => new(
              Success: false,
              Message: message,
              DeckNames: [],
              NoteTypeNames: [],
              ModelFields: new Dictionary<string, List<string>>(
                        StringComparer.Ordinal));

    private static bool IsRecognizedJetHelperNoteType(AnkiTemplateBundle bundle,
                                                      JsonElement templates,
                                                      JsonElement styling)
    {
        if (templates.ValueKind != JsonValueKind.Object
            || !templates.TryGetProperty(bundle.CardTemplateName,
                                         out var cardTemplate)
            || cardTemplate.ValueKind != JsonValueKind.Object)
            return false;

        var front = cardTemplate.TryGetProperty("Front", out var frontElement)
                              ? frontElement.GetString() ?? string.Empty
                              : string.Empty;
        var back = cardTemplate.TryGetProperty("Back", out var backElement)
                             ? backElement.GetString() ?? string.Empty
                             : string.Empty;
        var css = styling.ValueKind == JsonValueKind.Object
                                      && styling.TryGetProperty(
                                                "css", out var cssElement)
                            ? cssElement.GetString() ?? string.Empty
                            : string.Empty;

        return front.Contains(bundle.TemplateMarker, StringComparison.Ordinal)
               && back.Contains(bundle.TemplateMarker, StringComparison.Ordinal)
               && css.Contains("JETHelper Anki templates",
                               StringComparison.Ordinal);
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
