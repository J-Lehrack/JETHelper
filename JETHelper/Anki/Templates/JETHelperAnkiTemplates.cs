namespace JETHelper.Anki.Templates;

/// <summary>
/// Built-in, opt-in Anki note types.
///
/// The templates intentionally use only Anki's native field replacement,
/// conditional replacement, HTML, and CSS features. They contain no JavaScript,
/// external fonts, remote images, or other network-loaded resources.
/// </summary>
public static class JETHelperAnkiTemplates
{
    public const int TemplateVersion = 1;

    public const string VocabularyNoteTypeName = "JETHelper Vocabulary";
    public const string KanjiNoteTypeName = "JETHelper Kanji";
    public const string VocabularyDeckName = "JETHelper::Vocabulary";
    public const string KanjiDeckName = "JETHelper::Kanji";

    private const string CommonCss = """
/* JETHelper Anki templates — version 1 */

.card {
    --jet-background: #f4f2ed;
    --jet-surface: #ffffff;
    --jet-text: #242424;
    --jet-muted: #66645f;
    --jet-border: #d8d4ca;
    --jet-accent: #6b4f35;
    --jet-accent-soft: #eee5da;
    --jet-shadow: rgba(31, 27, 23, 0.08);

    box-sizing: border-box;
    margin: 0;
    padding: 24px 16px;
    color: var(--jet-text);
    background: var(--jet-background);
    font-family: Arial, Helvetica, sans-serif;
    font-size: 18px;
    line-height: 1.55;
    text-align: center;
}

.card.nightMode {
    --jet-background: #1d1d1f;
    --jet-surface: #29292c;
    --jet-text: #f1f0ed;
    --jet-muted: #bbb8b1;
    --jet-border: #454348;
    --jet-accent: #d8b98f;
    --jet-accent-soft: #3b332b;
    --jet-shadow: rgba(0, 0, 0, 0.28);
}

.jet-card {
    box-sizing: border-box;
    width: min(100%, 780px);
    margin: 0 auto;
}

.jet-card--back {
    padding: 18px;
    border: 1px solid var(--jet-border);
    border-radius: 14px;
    background: var(--jet-surface);
    box-shadow: 0 8px 24px var(--jet-shadow);
}

.jet-japanese {
    font-family: "Yu Mincho", "YuMincho", "Hiragino Mincho ProN",
                 "Noto Serif CJK JP", "Noto Serif JP", serif;
}

.jet-divider {
    width: min(100%, 780px);
    margin: 22px auto;
    border: 0;
    border-top: 1px solid var(--jet-border);
}

.jet-section {
    margin-top: 18px;
}

.jet-section:first-child {
    margin-top: 0;
}

.jet-section-title {
    margin: 0 0 8px;
    color: var(--jet-accent);
    font-size: 0.82rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
}

.jet-meta-row {
    display: flex;
    flex-wrap: wrap;
    justify-content: center;
    gap: 8px;
    margin-top: 16px;
}

.jet-pronunciation:empty,
.jet-readings:empty,
.jet-meta-row:empty {
    display: none;
}

.jet-meta {
    display: inline-flex;
    align-items: center;
    min-height: 28px;
    padding: 3px 10px;
    border: 1px solid var(--jet-border);
    border-radius: 999px;
    color: var(--jet-muted);
    background: var(--jet-accent-soft);
    font-size: 0.78rem;
}

.jet-example {
    padding: 14px 16px;
    border-left: 4px solid var(--jet-accent);
    border-radius: 8px;
    background: var(--jet-accent-soft);
    font-size: 1.08rem;
    text-align: left;
}

.jet-source {
    color: var(--jet-muted);
    font-size: 0.78em;
    white-space: normal;
}

.jet-pos {
    display: inline-block;
    margin-left: 4px;
    padding: 1px 6px;
    border-radius: 999px;
    color: var(--jet-muted);
    background: var(--jet-accent-soft);
    font-size: 0.72em;
    white-space: nowrap;
}

ruby rt {
    color: var(--jet-muted);
    font-size: 0.55em;
}

.replay-button svg {
    width: 32px;
    height: 32px;
}

.mobile .card {
    padding: 16px 10px;
}

.mobile .jet-card--back {
    padding: 14px;
    border-radius: 10px;
}
""";

    private const string VocabularyFront = """
<!-- JETHelper Vocabulary template v1 -->
<div class="jet-card jet-card--front">
    <div class="jet-expression jet-japanese">{{Expression}}</div>
</div>
""";

    private const string VocabularyBack = """
<!-- JETHelper Vocabulary template v1 -->
<div id="answer" class="jet-card jet-card--back">
    <div class="jet-pronunciation">{{#Furigana}}<div class="jet-furigana jet-japanese">{{furigana:Furigana}}</div>{{/Furigana}}{{^Furigana}}<div class="jet-furigana jet-japanese">{{Expression}}</div>{{/Furigana}}{{#Pitch Accent}}<div class="jet-pitch jet-japanese">{{Pitch Accent}}</div>{{/Pitch Accent}}{{#Audio}}<div class="jet-audio">{{Audio}}</div>{{/Audio}}</div>

    <div class="jet-definitions">
        {{#Meaning English}}
        <section class="jet-definition-section">
            <h2 class="jet-section-title">English</h2>
            <div class="jet-definition-content">{{Meaning English}}</div>
        </section>
        {{/Meaning English}}

        {{#Meaning Japanese}}
        <section class="jet-definition-section">
            <h2 class="jet-section-title">Japanese</h2>
            <div class="jet-definition-content jet-japanese">{{Meaning Japanese}}</div>
        </section>
        {{/Meaning Japanese}}

        {{#Meaning Slang}}
        <section class="jet-definition-section">
            <h2 class="jet-section-title">Slang / Media</h2>
            <div class="jet-definition-content">{{Meaning Slang}}</div>
        </section>
        {{/Meaning Slang}}
    </div>

    {{#Frequency}}
    <div class="jet-meta-row">
        <div class="jet-meta">Frequency rank: {{Frequency}}</div>
    </div>
    {{/Frequency}}

    {{#Sentence}}
    <section class="jet-section">
        <h2 class="jet-section-title">Example</h2>
        <div class="jet-example jet-japanese">{{furigana:Sentence}}</div>
    </section>
    {{/Sentence}}
</div>
""";

    private const string VocabularyCss = CommonCss + """

.jet-expression {
    display: flex;
    min-height: 190px;
    align-items: center;
    justify-content: center;
    font-size: clamp(3rem, 12vw, 5.4rem);
    font-weight: 600;
    line-height: 1.15;
    overflow-wrap: anywhere;
}

.jet-pronunciation {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: center;
    gap: 10px 18px;
}

.jet-furigana {
    font-size: clamp(1.45rem, 5vw, 2rem);
    line-height: 1.45;
}

.jet-pitch {
    color: var(--jet-muted);
    font-size: 1rem;
}

.jet-audio {
    display: flex;
    align-items: center;
}

.jet-definitions {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
    gap: 14px;
    margin-top: 18px;
    text-align: left;
}

.jet-definition-section {
    min-width: 0;
    padding: 14px;
    border: 1px solid var(--jet-border);
    border-radius: 10px;
    background: var(--jet-background);
}

.jet-definition-content ul {
    margin: 0;
    padding-left: 1.25rem;
}

.jet-definition-content li {
    margin: 0.35rem 0;
    break-inside: avoid;
}

.mobile .jet-expression {
    min-height: 145px;
}
""";

    private const string KanjiFront = """
<!-- JETHelper Kanji template v1 -->
<div class="jet-card jet-card--front">
    <div class="jet-kanji-character jet-japanese">{{Kanji Character}}</div>
</div>
""";

    private const string KanjiBack = """
<!-- JETHelper Kanji template v1 -->
{{FrontSide}}
<hr id="answer" class="jet-divider">

<div class="jet-card jet-card--back">
    {{#Meaning}}
    <section class="jet-section">
        <h2 class="jet-section-title">Meaning</h2>
        <div class="jet-kanji-meaning">{{Meaning}}</div>
    </section>
    {{/Meaning}}

    {{#Diagram}}
    <div class="jet-diagram jet-japanese">{{Diagram}}</div>
    {{/Diagram}}

    <div class="jet-readings">{{#Kunyomi}}<div class="jet-reading-row"><span class="jet-reading-label">訓読み</span><span class="jet-reading-value jet-japanese">{{Kunyomi}}</span></div>{{/Kunyomi}}{{#Onyomi}}<div class="jet-reading-row"><span class="jet-reading-label">音読み</span><span class="jet-reading-value jet-japanese">{{Onyomi}}</span></div>{{/Onyomi}}</div>

    <div class="jet-meta-row">{{#Strokes}}<div class="jet-meta">{{Strokes}}</div>{{/Strokes}}{{#Frequency}}<div class="jet-meta">Frequency rank: {{Frequency}}</div>{{/Frequency}}</div>

    {{#Sentence}}
    <section class="jet-section">
        <h2 class="jet-section-title">例文</h2>
        <div class="jet-example jet-japanese">{{furigana:Sentence}}</div>
    </section>
    {{/Sentence}}
</div>
""";

    private const string KanjiCss = CommonCss + """

.jet-kanji-character {
    display: flex;
    min-height: 210px;
    align-items: center;
    justify-content: center;
    font-size: clamp(4.5rem, 18vw, 8rem);
    font-weight: 500;
    line-height: 1;
}

.jet-kanji-meaning {
    font-size: 1.1rem;
    line-height: 1.7;
}

.jet-diagram {
    max-width: 260px;
    margin: 20px auto 0;
    overflow: hidden;
}

.jet-diagram img,
.jet-diagram svg {
    display: block;
    max-width: 100%;
    height: auto;
    margin: 0 auto;
}

.jet-readings {
    display: grid;
    gap: 10px;
    margin-top: 20px;
    text-align: left;
}

.jet-reading-row {
    display: grid;
    grid-template-columns: 5rem minmax(0, 1fr);
    align-items: baseline;
    gap: 10px;
    padding: 10px 12px;
    border: 1px solid var(--jet-border);
    border-radius: 9px;
    background: var(--jet-background);
}

.jet-reading-label {
    color: var(--jet-accent);
    font-size: 0.78rem;
    font-weight: 700;
}

.jet-reading-value {
    font-size: 1.12rem;
    overflow-wrap: anywhere;
}

@media (max-width: 540px) {
    .jet-reading-row {
        grid-template-columns: 4.5rem minmax(0, 1fr);
    }
}

.mobile .jet-kanji-character {
    min-height: 160px;
}
""";

    public static readonly AnkiTemplateBundle Vocabulary = new(
        NoteTypeName: VocabularyNoteTypeName,
        CardTemplateName: "Vocabulary Card",
        TemplateMarker: "JETHelper Vocabulary template",
        Fields:
        [
            "Expression",
            "Furigana",
            "Meaning English",
            "Meaning Japanese",
            "Meaning Slang",
            "Audio",
            "Frequency",
            "Sentence",
            "Pitch Accent"
        ],
        FrontTemplate: VocabularyFront,
        BackTemplate: VocabularyBack,
        Css: VocabularyCss);

    public static readonly AnkiTemplateBundle Kanji = new(
        NoteTypeName: KanjiNoteTypeName,
        CardTemplateName: "Kanji Card",
        TemplateMarker: "JETHelper Kanji template",
        Fields:
        [
            "Kanji Character",
            "Meaning",
            "Kunyomi",
            "Onyomi",
            "Frequency",
            "Sentence",
            "Strokes",
            "Diagram"
        ],
        FrontTemplate: KanjiFront,
        BackTemplate: KanjiBack,
        Css: KanjiCss);
}
