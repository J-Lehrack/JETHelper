# JETHelper

JETHelper is a Dalamud plugin for Japanese learners playing FINAL FANTASY XIV. It turns copied or manually entered Japanese text into vocabulary and kanji lookup results, then sends selected entries to configurable Anki decks through AnkiConnect.

> **Project status:** Functional alpha. The core lookup-to-Anki workflow works, but installation, dictionary distribution, deinflection, candidate ranking, and media fields still need refinement before a general release.

## Current features

- Manual Japanese text lookup through the in-game window or slash commands.
- Explicit clipboard lookup through a configurable keyboard shortcut.
- Vocabulary and individual-kanji candidates that preserve the original sentence context.
- English, Japanese, slang/media, frequency, reading, and stroke data when supported dictionaries provide it.
- Configurable vocabulary and kanji decks, note types, and field mappings.
- Anki card creation through AnkiConnect, including duplicate prevention and required-field validation.
- Persistent plugin settings through Dalamud.

## Requirements

- FINAL FANTASY XIV launched through XIVLauncher with Dalamud enabled.
- Compatible Yomitan-format dictionary archives.
- Anki Desktop with the AnkiConnect add-on for card creation. Dictionary lookup itself does not require Anki.

## Commands

| Command | Purpose |
| --- | --- |
| `/jet` | Open or close the lookup window. Text after the command is processed immediately. |
| `/jetlookup <text>` | Process Japanese text directly. |
| `/jetclip` | Process the current clipboard text. |
| `/jetconfig` | Open hotkey, dictionary, and Anki connection settings. |
| `/jetcardconfig` | Open Anki field-mapping settings. |
| `/jetabout` | Open acknowledgements, licences, and bundled data-source information. |
| `/jetdebug` | Open service health, recent diagnostic events, and local log controls. |

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

JETHelper currently permits only the approved bundled archives `jmdict_english.zip` and `kanjidic_english.zip` to be included in release output. Other compatible dictionaries may be supplied by the user through `/jetconfig` and are never included automatically. See `JETHelper/Assets/Dictionaries/README.md`.

## Dictionary data and acknowledgements

JETHelper bundles Yomitan-compatible versions of **JMdict (English)** and **KANJIDIC/KANJIDIC2 (English)**, whose underlying data is maintained by the Electronic Dictionary Research and Development Group (EDRDG).

See [`ACKNOWLEDGEMENTS.md`](ACKNOWLEDGEMENTS.md) for dictionary source links, the EDRDG licence page, the Yomitan conversion project, related tools, contributors, and dictionary update information. The same information is available in game through `/jetabout`.

Additional dictionaries selected by users are not distributed by JETHelper and remain subject to their respective terms.

## Anki setup

1. Install and enable AnkiConnect in Anki Desktop.
2. Keep Anki open while using card-export features.
3. Open `/jetconfig` and refresh the AnkiConnect connection.
4. Select vocabulary and kanji decks and note types.
5. Open `/jetcardconfig` to map JETHelper data roles to your existing Anki fields.

JETHelper does not modify the styling of existing note types. Bundled optional templates and styling are planned for a later phase.

## Privacy and network behavior

- Clipboard text is read only when the user explicitly triggers a lookup.
- Dictionary lookups are performed locally from the configured archives.
- Anki requests use the configured AnkiConnect address, which defaults to `http://127.0.0.1:8765`.
- JETHelper can write a local troubleshooting log in its Dalamud configuration folder. Lookup text is excluded by default and is included only when the user explicitly enables that diagnostic option.

## Diagnostics and bug reports

Use `/jetdebug` to view dictionary-service health, recent structured events, and the location of `JETHelper.log`. The log records dictionary discovery, load failures, lookup timings, and AnkiConnect failures. It is intended for troubleshooting and GitHub issue reports rather than normal lookup use.

## Known limitations

- Deinflection currently handles only a limited set of common conjugations.
- Vocabulary candidate detection is intentionally permissive and may show substring matches.
- Audio, pitch-accent diagrams, and kanji stroke diagrams are not yet populated.
- AnkiConnect requests are currently synchronous and can cause a brief stutter when Anki is unavailable.
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

Near-term work includes optional JETHelper Anki templates/CSS, stronger deinflection, better candidate ranking, and release-safe dictionary installation.

## License

JETHelper source code is licensed under the GNU Affero General Public License v3.0 or later. Third-party dictionaries and other data files retain their own licenses and are not covered by the JETHelper source-code license.
