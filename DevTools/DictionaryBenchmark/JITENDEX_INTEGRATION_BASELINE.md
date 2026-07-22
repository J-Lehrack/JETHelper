# Phase 6C Selected Dictionary Baseline

This record identifies the exact archives inspected for the Phase 6C Jitendex transition. It is a development reference, not a redistribution approval. Licence and attribution review remains a separate checklist item.

## Selected transition folder

| Archive | Dictionary title / revision | SHA-256 | Raw structure |
| --- | --- | --- | --- |
| `[JA-EN] jitendex-yomitan (2026-07-09).zip` | `Jitendex.org [2026-07-09]` / `2026.07.09.0` | `807d911114af9d2154d270702972aafb2b6a6c2dc2400afa98db870d035c1a0b` | 217 term banks, 433,885 term rows, 1 tag bank |
| `[JA-EN] jmdict_english.zip` | `JMdict (English)` / `jmdict4` | `df5d13b376863d2074620acac9ce04666d8a40f69ab6d99dc7aa280dacc9fe52` | 29 term banks, 284,196 term rows, 1 tag bank |
| `[Kanji] KANJIDIC_english (2026-07-12).zip` | `KANJIDIC [2026-193]` / `kanjidic2.2026-193` | `04eac065083c38cfe5fed09f8ca9f8163a77a58044857122904398e8732051d6` | 2 kanji banks, 10,384 kanji rows, 1 tag bank |
| `[JA Freq] jiten_freq_global (2026-07-13).zip` | `Jiten` / `Jiten 26-07-11` | `19f62a4b1cada4c4cd01f8d3002bd71942971b8b695ee0938adf4105b1bfc711` | 1 term-metadata bank, 584,227 rows |
| `[Kanji] mozc Kanji Variants.zip` | `mozc Kanji Variants` / `mozc_2022-08-26T22:38:27.927Z` | `44a980885ee852b124bb3f37fd67a0bd320474234013b62f3d98b108c0752cff` | 1 kanji bank, 1,317 rows, 1 tag bank |

## Jitendex structured-content findings

The selected Jitendex archive contains one structured-content object for every term row. The inspected semantic markers include:

- sense groups and individual senses;
- part-of-speech, miscellaneous, field, and dialect tags;
- glossary lists;
- sense notes and language-source notes;
- cross-references and antonyms;
- spelling/reading form tables;
- 50,758 attributed example-sentence blocks;
- redirects for alternate, rare, old, and variant forms;
- entry attribution links.

The archive contains 251 non-JSON files:

- 201 AVIF graphics;
- 48 SVG glyph assets;
- 1 CSS file;
- 1 HanaMinA licence file.

The exact archive contains:

- no `term_meta_bank_` pitch-accent bank;
- no audio file;
- no audio URL/reference detected in structured metadata.

JETHelper therefore keeps pitch and audio empty for this archive rather than inferring values. Graphics remain outside the first structured-definition integration pass.

## Mozc handling decision

Mozc stores variant explanations in the normal kanji meaning column. Phase 6C keeps those blocks as separate variant notes instead of merging them into KANJIDIC's ordinary English meanings. Supplementary-plane CJK characters such as `𠮟` are included in Japanese detection and kanji lookup.
