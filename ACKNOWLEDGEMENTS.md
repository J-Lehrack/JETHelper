# Acknowledgements

JETHelper is built with help from open-source projects, community-maintained
resources, and dictionary data made available for Japanese-language study.

## Bundled dictionaries

JETHelper currently bundles these reviewed Yomitan-compatible archives:

- **JMdict (English)** — Japanese vocabulary definitions and readings.
- **KANJIDIC/KANJIDIC2 (English)** — Japanese kanji meanings, readings, and
  metadata.
- **Jiten Frequency Global** — global Japanese word-frequency ranks.

### JMdict and KANJIDIC

The underlying JMdict and KANJIDIC/KANJIDIC2 data is maintained by the
**Electronic Dictionary Research and Development Group (EDRDG)**.

- [EDRDG website](https://www.edrdg.org/)
- [EDRDG dictionary licence](https://www.edrdg.org/edrdg/licence.html)
- [JMdict/EDICT Dictionary Project](https://www.edrdg.org/wiki/index.php/JMdict-EDICT_Dictionary_Project)
- [KANJIDIC Project](https://www.edrdg.org/wiki/index.php/KANJIDIC_Project)

The bundled Yomitan-compatible JMdict and KANJIDIC archives are obtained from
the community-maintained
[yomidevs/jmdict-yomitan](https://github.com/yomidevs/jmdict-yomitan)
releases.

### Jiten Frequency Global

Jiten Frequency Global is created and maintained by the **Jiten** project from
frequency data covering Japanese media tracked by Jiten.

- [Jiten](https://jiten.moe/)
- [Jiten frequency-list downloads](https://jiten.moe/other)
- [Creative Commons Attribution-ShareAlike 4.0](https://creativecommons.org/licenses/by-sa/4.0/)

Jiten states that its downloadable frequency lists are licensed under
CC BY-SA 4.0.

JETHelper does not claim ownership of any bundled dictionary data. Each archive
remains subject to its applicable terms and attribution requirements.

## Updating dictionary data

Bundled JMdict, KANJIDIC, and Jiten Frequency Global snapshots may be refreshed
as part of future JETHelper releases.

Users may also supply additional compatible dictionaries through the extra
dictionary folder configured in `/jetconfig`. JETHelper does not distribute
those user-supplied dictionaries, and each source remains subject to its own
terms.

## Community dictionary resources

Users looking for other compatible dictionaries may consult:

- [MarvNC's Yomitan dictionary collection](https://github.com/MarvNC/yomitan-dictionaries)
- [MarvNC's dictionary download folder](https://drive.google.com/drive/folders/1LXMIOoaWASIntlx1w08njNU005lS5lez)

Only the dictionaries explicitly listed as bundled by JETHelper have been
reviewed for JETHelper's own distribution and tested as part of its supported
baseline. Other downloads are external community resources. Users are
responsible for reviewing their provenance, licensing, compatibility, size,
and suitability before adding them.

## Related projects and resources

- [Yomitan](https://github.com/themoeway/yomitan) — browser-based Japanese
  lookup tooling and the dictionary format that inspired JETHelper's lookup
  workflow.
- [Anki](https://apps.ankiweb.net/) — spaced-repetition flashcard software.
- [AnkiConnect](https://github.com/FooSoft/anki-connect) — local API used by
  JETHelper to create Anki notes.
- [Dalamud](https://github.com/goatcorp/Dalamud) — the FFXIV plugin framework
  used by JETHelper.

## Contributors

JETHelper was created by **Ardianell**
([@J-Lehrack](https://github.com/J-Lehrack)).

Additional contributors will be acknowledged here as the project grows.

## JETHelper source code

JETHelper's own source code is licensed under the GNU Affero General Public
License version 3.0 or later. See [`LICENSE.md`](LICENSE.md).
