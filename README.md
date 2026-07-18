# JETHelper

JETHelper is a Dalamud plugin for Japanese learners playing FINAL FANTASY XIV. It turns copied or manually entered Japanese text into vocabulary and kanji lookup results, then sends selected entries to configurable Anki decks through AnkiConnect.

> **Project status:** Functional alpha. The core lookup-to-Anki workflow, bundled dictionaries, diagnostics, optional Anki note types, non-blocking AnkiConnect operations, background dictionary loading, benchmark instrumentation, and three-machine dictionary performance study are complete. Final lifecycle regression, the planned Jitendex transition, comprehensive deinflection, candidate ranking, and UI refinement remain before a general beta release.

## Current features

- Manual Japanese text lookup through the in-game window or slash commands.
- Explicit clipboard lookup through a configurable keyboard shortcut.
- Vocabulary and individual-kanji candidates that preserve the original sentence context.
- English, Japanese, slang/media, frequency, reading, and stroke data when supported dictionaries provide it.
- Configurable vocabulary and kanji decks, note types, and field mappings.
- Anki card creation through AnkiConnect, including duplicate prevention and required-field validation.
- Optional JETHelper vocabulary and kanji note types with responsive, night-mode-aware styling.
- Non-blocking dictionary discovery, validation, parsing, indexing, and AnkiConnect operations.
- Persistent plugin settings through Dalamud.

## Requirements

- FINAL FANTASY XIV launched through XIVLauncher with Dalamud enabled.
- Compatible Yomitan-format dictionary archives.
- Anki Desktop with the AnkiConnect add-on for card creation. Dictionary lookup itself does not require Anki.

For the overall FFXIV environment, **16 GB of system memory is a practical baseline for the curated JETHelper dictionary profile under the tested conditions**. **32 GB is recommended for larger personal dictionary collections, frequent reloads, or heavier multitasking.** These are usage guidelines rather than hard plugin-enforced requirements.

## Commands

| Command             | Purpose                                                                           |
| ------------------- | --------------------------------------------------------------------------------- |
| `/jet`              | Open or close the lookup window. Text after the command is processed immediately. |
| `/jetlookup <text>` | Process Japanese text directly.                                                   |
| `/jetclip`          | Process the current clipboard text.                                               |
| `/jetconfig`        | Open hotkey, dictionary, and Anki connection settings.                            |
| `/jetcardconfig`    | Open Anki field-mapping settings.                                                 |
| `/jetabout`         | Open acknowledgements, licences, and bundled data-source information.             |
| `/jetdebug`         | Open service health, recent diagnostic events, and local log controls.            |
| `/jetbenchmark`     | Open development-only dictionary runtime benchmark controls.                      |

## Basic use

1. Copy Japanese text from the FFXIV chat log, or type text into the JETHelper lookup window.
2. Trigger the configured clipboard hotkey, run `/jetclip`, or click **Process Clipboard**.
3. Select a vocabulary or kanji candidate.
4. Review the available dictionary data.
5. Click **Add Vocab Card** or **Add Kanji Card** after configuring AnkiConnect.

Dalamud normally restricts plugin windows during some in-game cutscenes. Enable Dalamud's setting that allows plugin UI during cutscenes if you want to use JETHelper while story dialogue is playing.

## Dictionary setup

During development, place legally obtained dictionary ZIP files in:

```text
JETHelper/Assets/Dictionaries/
```

At runtime, JETHelper first checks its installed plugin assets and then falls back to the custom dictionary folder configured through `/jetconfig`.

Dictionary discovery, archive validation, parsing, and lookup-index construction run on one serialized background worker. Saving, clearing, or reloading the dictionary path returns immediately. During a manual reload, the previously active fully indexed snapshot remains usable until the complete replacement is ready. New snapshots are activated only after all supported vocabulary, kanji, and frequency indexes finish building.

JETHelper currently permits only the approved bundled archives `jmdict_english.zip`, `kanjidic_english.zip`, and `jiten_freq_global.zip` to be included in release output. Other compatible dictionaries may be supplied by the user through `/jetconfig` and are never included automatically. See `JETHelper/Assets/Dictionaries/README.md`.

When the same dictionary title and revision are found more than once, JETHelper prefers the healthier readable copy first and then prefers a user-configured copy over an equally healthy bundled copy. Different revisions are loaded as separate sources rather than silently replacing one another.

### Dictionary performance and memory guidance

JETHelper loads enabled dictionary indexes into memory so lookups remain immediate after the background load completes. Dictionary count alone is not a reliable measure of cost: one encyclopedia-scale archive may require more resources than numerous small frequency dictionaries.

The completed three-machine benchmark covered a 32 GB desktop, a newer 32 GB laptop, and an older 16 GB laptop. Under stable FFXIV inn-room conditions:

- the current curated bundle loaded in roughly **2–3 seconds**;
- expanded and lightweight personal profiles loaded in roughly **12–23 seconds**, depending on hardware and contents;
- a deliberately excessive heavyweight profile took roughly **35–37 seconds** and used several additional gigabytes of memory.

During a manual reload, the current and replacement snapshots temporarily coexist. Large custom collections can therefore approach roughly twice their steady-state snapshot cost before the completed replacement is activated and the old snapshot becomes collectible.

Practical guidance:

- add optional dictionaries gradually;
- avoid loading heavily overlapping general dictionaries unless both provide clear value;
- expect large encyclopedia dictionaries to take noticeably longer and require more memory;
- use 32 GB of RAM for comfortable headroom with large collections and multiple other applications;
- treat heavyweight collections as advanced, user-managed configurations.

JETHelper does not impose an arbitrary dictionary-count or memory limit. The current evidence does not justify a disk-backed index, forced source disabling, or intrusive hardware detection. Per-dictionary enable/disable controls remain a possible later enhancement if real user demand justifies them.

### Planned bundled-dictionary transition

The current working bundle remains **JMdict English + KANJIDIC English + Jiten Frequency Global** until its replacement passes integration and regression testing.

The planned curated profile is:

```text
Jitendex
KANJIDIC English
Jiten Frequency Global
Mozc Kanji Variants
```

Jitendex is intended to **replace**, not accompany, bundled JMdict because the two sources overlap heavily. The transition will occur only after JETHelper supports the selected Jitendex archive's structured definitions, grammar and usage information, examples, pitch data, available audio workflow, attribution, packaging, lookup display, and Anki export.

Generic proper-name dictionaries such as JMnedict are not planned for the default bundle. Their broad real-world name coverage has limited value for FFXIV's largely fictional and game-specific names. Optional name-dictionary support remains a possible specialized feature, but it is not a core lookup or external-browser-text requirement.

## Dictionary data and acknowledgements

JETHelper currently bundles Yomitan-compatible versions of **JMdict (English)** and **KANJIDIC/KANJIDIC2 (English)**, whose underlying data is maintained by the Electronic Dictionary Research and Development Group (EDRDG), plus **Jiten Frequency Global**, maintained by the Jiten project and distributed under CC BY-SA 4.0.

See [`ACKNOWLEDGEMENTS.md`](ACKNOWLEDGEMENTS.md) for dictionary source links, the EDRDG licence page, the Yomitan conversion project, related tools, contributors, and dictionary update information. The same information is available in game through `/jetabout`.

Additional dictionaries selected by users are not distributed by JETHelper and remain subject to their respective terms.

For users who want more dictionary options, `ACKNOWLEDGEMENTS.md` and `/jetabout` link to MarvNC's Yomitan dictionary repository and download folder. Only JETHelper's explicitly bundled dictionaries have been reviewed for JETHelper distribution and tested as its supported baseline; other downloads are used at the user's discretion.

## Anki setup

1. Install and enable AnkiConnect in Anki Desktop.
2. Keep Anki open while using card-export features.
3. Open `/jetconfig` and refresh the AnkiConnect connection.
4. Select vocabulary and kanji decks and note types.
5. Open `/jetcardconfig` to map JETHelper data roles to your existing Anki fields.

After a successful refresh, expand **Optional JETHelper note types** to:

- create `JETHelper Vocabulary`;
- create `JETHelper Kanji`;
- independently create and select the recommended decks `JETHelper::Vocabulary` and `JETHelper::Kanji`.

Deck creation is a separate action, so a conflicting note-type name cannot prevent the recommended decks from being created.

The note-type installers create new note types only. If an exact-name note type already exists, JETHelper leaves it unchanged. Previously installed JETHelper templates are recognized and may be selected again; an unrelated same-name note type is not modified or automatically selected. Empty optional fields are hidden with native Anki conditional replacements, so labels such as **Japanese**, **Slang / Media**, **Frequency**, and **Example** appear only when their fields contain data.

The bundled templates use only local HTML and CSS. They contain no JavaScript, remote fonts, or external web resources.

AnkiConnect refresh, card creation, recommended-deck creation, and optional note-type installation run asynchronously. JETHelper shows an in-progress state and disables the active action so a slow or unavailable Anki instance does not freeze the game UI or receive duplicate requests.

## Privacy and network behavior

- Clipboard text is read only when the user explicitly triggers a lookup.
- Dictionary lookups are performed locally from the configured archives.
- Anki requests use the configured AnkiConnect address, which defaults to `http://127.0.0.1:8765`.
- JETHelper can write a local troubleshooting log in its Dalamud configuration folder. Lookup text is excluded by default and is included only when the user explicitly enables that diagnostic option.
- Benchmark output can include local dictionary paths and runtime metadata, but it does not include lookup text or automatically enumerate detailed hardware, machine names, or enabled plugins.

## Diagnostics and bug reports

Use `/jetdebug` to view dictionary-service health, recent structured events, and the location of `JETHelper.log`. The log records dictionary discovery, load failures, lookup timings, and AnkiConnect failures. It is intended for troubleshooting and GitHub issue reports rather than normal lookup use.

## Development dictionary benchmarks

Use `/jetbenchmark` only during deliberate development measurements. A benchmark run instruments one dictionary reload and writes structured newline-delimited JSON to `JETHelper.dictionary-benchmark.jsonl` in the Dalamud plugin configuration folder. Normal reloads do not start the memory sampler or collect per-row loader metrics.

The benchmark window supports:

- a manual benchmark reload;
- a development-only cancellation control for the active benchmark reload;
- a one-shot benchmark armed for the next plugin startup;
- stable profile labels for comparing machines and dictionary sets;
- per-source validation and indexing measurements;
- retained lookup-key and result-object counts from the real JETHelper loaders;
- managed heap, process working-set, private-memory, and observed peak measurements;
- an explicit post-GC capture after a replacement run.

The full-GC and cancellation controls are development-only. Cancellation preserves the previously active dictionary snapshot and records a terminal `cancelled` benchmark outcome after the background worker reaches a cancellation check. Managed-memory and process-memory values describe the entire FFXIV process—including Dalamud and other loaded plugins—not allocations owned exclusively by JETHelper, so benchmark conditions should remain stable. The output contains dictionary paths and system/runtime metadata, but never lookup text.

`DevTools/DictionaryBenchmark/analyze_runtime_benchmarks.py` validates benchmark lifecycles and produces per-run plus workload-grouped JSON, CSV, and Markdown summaries. It groups uniquely named repeated runs by the actual indexed source/count signature rather than requiring identical user-entered labels.

The completed combined benchmark dataset contained **63 runs: 61 successful, 2 intentionally cancelled, 0 failed, and no lifecycle or reconciliation findings**. A reduced confirmation benchmark will be repeated after the exact Jitendex-based curated profile is integrated.

## Known limitations

- Deinflection currently handles only a limited set of common conjugations.
- Vocabulary candidate detection is intentionally permissive and may show substring matches.
- Jitendex-specific structured grammar, examples, pitch accent, and audio are not yet integrated.
- Kanji stroke diagrams are not yet populated.
- Very large collections can require significant background CPU, memory, and total loading time, especially during replacement reloads where two snapshots temporarily coexist.
- Direct reading of selected chat text is not implemented; the current workflow uses copied text.

## Building from source

JETHelper targets Dalamud API 15 and .NET 10.

1. Install the .NET 10 SDK and a compatible Visual Studio or Rider version.
2. Clone the repository.
3. Add any local dictionary archives under `JETHelper/Assets/Dictionaries/`.
4. Open `JETHelper.sln` and build the `Debug` or `Release` configuration.
5. Add the resulting `JETHelper.dll` as a Dalamud development plugin.

Typical debug output:

```text
JETHelper/bin/x64/Debug/JETHelper.dll
```

## Roadmap

The next checkpoint is the final responsiveness and lifecycle regression pass. After that, planned work includes the Jitendex/JMdict bundle transition, comprehensive deinflection, candidate ranking, and lookup UI refinement. See [`CHECKLIST.md`](CHECKLIST.md) for the active roadmap and completed-phase history.

## License

JETHelper source code is licensed under the GNU Affero General Public License v3.0 or later. Third-party dictionaries and other data files retain their own licenses and are not covered by the JETHelper source-code license.
