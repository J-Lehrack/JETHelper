"""
JETHelper Dictionary Test Generator

Run this file directly from PyCharm or with:

    python main.py

It creates a fresh `generated/` folder containing small synthetic Yomitan
dictionary archives for manual and automated regression testing.

Only Python's standard library is required.
"""

from __future__ import annotations

import json
import shutil
import zipfile
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
OUTPUT_DIR = SCRIPT_DIR / "generated"


def write_zip(
    output_path: Path,
    files: dict[str, str | bytes],
    *,
    compression: int = zipfile.ZIP_DEFLATED,
) -> None:
    """Create a ZIP archive from a mapping of archive paths to contents."""
    output_path.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(output_path, "w", compression=compression) as archive:
        for archive_name, content in files.items():
            if isinstance(content, str):
                content = content.encode("utf-8")

            archive.writestr(archive_name, content)


def to_json(value: Any) -> str:
    """Serialize JSON in a predictable, readable form."""
    return json.dumps(value, ensure_ascii=False, indent=2)


def term_index(title: str, revision: str = "1") -> dict[str, Any]:
    """Create minimal Yomitan-style term dictionary metadata."""
    return {
        "title": title,
        "revision": revision,
        "format": 3,
        "sequenced": False,
        "author": "JETHelper Test Generator",
        "description": "Synthetic test data generated for JETHelper.",
    }


def kanji_index(title: str, revision: str = "1") -> dict[str, Any]:
    """Create minimal Yomitan-style kanji dictionary metadata."""
    return {
        "title": title,
        "revision": revision,
        "format": 3,
        "sequenced": False,
        "author": "JETHelper Test Generator",
        "description": "Synthetic kanji test data generated for JETHelper.",
    }


def valid_term_bank(entries: list[list[Any]] | None = None) -> list[list[Any]]:
    """
    Return a minimal Yomitan term bank.

    Entry shape:
    [expression, reading, definitionTags, rules, score, definitions, sequence, termTags]
    """
    return entries or [
        [
            "食べる",
            "たべる",
            "",
            "v1",
            0,
            ["to eat"],
            1,
            "",
        ],
        [
            "猫",
            "ねこ",
            "",
            "n",
            0,
            ["cat"],
            2,
            "",
        ],
    ]


def valid_kanji_bank() -> list[list[Any]]:
    """
    Return a minimal Yomitan kanji bank.

    Entry shape:
    [character, onyomi, kunyomi, tags, meanings, stats]
    """
    return [
        [
            "食",
            "ショク",
            "た.べる",
            "",
            ["eat", "food"],
            {"strokes": "9"},
        ],
        [
            "猫",
            "ビョウ",
            "ねこ",
            "",
            ["cat"],
            {"strokes": "11"},
        ],
    ]


def valid_tag_bank() -> list[list[Any]]:
    """Return a minimal tag bank."""
    return [
        ["n", "partOfSpeech", 0, "noun", 0],
        ["v1", "partOfSpeech", 0, "Ichidan verb", 0],
    ]


def build_cases() -> list[tuple[str, str]]:
    """Generate all test archives and return their names/descriptions."""
    cases: list[tuple[str, str]] = []

    valid_dir = OUTPUT_DIR / "Valid"
    invalid_dir = OUTPUT_DIR / "Invalid"
    partial_dir = OUTPUT_DIR / "Partial"
    duplicate_dir = OUTPUT_DIR / "Duplicates"

    # 1. Valid term dictionary
    write_zip(
        valid_dir / "ValidTermDictionary.zip",
        {
            "index.json": to_json(term_index("JETHelper Valid Terms", "1")),
            "term_bank_1.json": to_json(valid_term_bank()),
            "tag_bank_1.json": to_json(valid_tag_bank()),
        },
    )
    cases.append((
        "Valid/ValidTermDictionary.zip",
        "Fully valid small term dictionary.",
    ))

    # 2. Valid kanji dictionary
    write_zip(
        valid_dir / "ValidKanjiDictionary.zip",
        {
            "index.json": to_json(kanji_index("JETHelper Valid Kanji", "1")),
            "kanji_bank_1.json": to_json(valid_kanji_bank()),
        },
    )
    cases.append((
        "Valid/ValidKanjiDictionary.zip",
        "Fully valid small kanji dictionary.",
    ))

    # 3. Missing index
    write_zip(
        invalid_dir / "MissingIndex.zip",
        {
            "term_bank_1.json": to_json(valid_term_bank()),
        },
    )
    cases.append((
        "Invalid/MissingIndex.zip",
        "Valid bank data but no index.json.",
    ))

    # 4. Malformed index JSON
    write_zip(
        invalid_dir / "MalformedIndexJson.zip",
        {
            "index.json": '{"title": "Broken Index" "format": 3}',
            "term_bank_1.json": to_json(valid_term_bank()),
        },
    )
    cases.append((
        "Invalid/MalformedIndexJson.zip",
        "Valid ZIP with deliberately malformed index.json.",
    ))

    # 5. Index has wrong JSON type
    write_zip(
        invalid_dir / "IndexIsArray.zip",
        {
            "index.json": to_json(["index", "must", "be", "an", "object"]),
            "term_bank_1.json": to_json(valid_term_bank()),
        },
    )
    cases.append((
        "Invalid/IndexIsArray.zip",
        "index.json parses, but its root is an array instead of an object.",
    ))

    # 6. Malformed term bank JSON
    write_zip(
        invalid_dir / "MalformedTermBankJson.zip",
        {
            "index.json": to_json(term_index("JETHelper Malformed Term Bank", "1")),
            "term_bank_1.json": '[["食べる", "たべる", "", "v1", 0, ["to eat"]]',
        },
    )
    cases.append((
        "Invalid/MalformedTermBankJson.zip",
        "Valid index with malformed term-bank JSON.",
    ))

    # 7. Term bank wrong JSON root type
    write_zip(
        invalid_dir / "TermBankIsObject.zip",
        {
            "index.json": to_json(term_index("JETHelper Wrong Bank Type", "1")),
            "term_bank_1.json": to_json({"not": "an array"}),
        },
    )
    cases.append((
        "Invalid/TermBankIsObject.zip",
        "term_bank_1.json parses, but its root is an object instead of an array.",
    ))

    # 8. No recognized banks
    write_zip(
        invalid_dir / "NoRecognizedBanks.zip",
        {
            "index.json": to_json(term_index("JETHelper No Banks", "1")),
            "readme.txt": "This archive has metadata but no recognized Yomitan banks.",
        },
    )
    cases.append((
        "Invalid/NoRecognizedBanks.zip",
        "Valid metadata but no recognized dictionary banks.",
    ))

    # 9. Unsupported bank layout
    write_zip(
        invalid_dir / "UnsupportedBankLayout.zip",
        {
            "index.json": to_json(term_index("JETHelper Unsupported Layout", "1")),
            "custom_bank_1.json": to_json([["custom", "data"]]),
        },
    )
    cases.append((
        "Invalid/UnsupportedBankLayout.zip",
        "Contains an unknown bank filename rather than a supported Yomitan bank.",
    ))

    # 10. Completely broken ZIP
    broken_zip = invalid_dir / "CorruptedZip.zip"
    broken_zip.parent.mkdir(parents=True, exist_ok=True)
    broken_zip.write_bytes(
        b"PK\x03\x04This deliberately is not a complete ZIP archive.\x00\xff\x13\x37"
    )
    cases.append((
        "Invalid/CorruptedZip.zip",
        "Not a structurally valid ZIP archive.",
    ))

    # 11. Truncated copy of a valid archive
    good_bytes_path = valid_dir / "ValidTermDictionary.zip"
    truncated_path = invalid_dir / "TruncatedValidZip.zip"
    data = good_bytes_path.read_bytes()
    truncated_path.write_bytes(data[: max(32, len(data) // 2)])
    cases.append((
        "Invalid/TruncatedValidZip.zip",
        "A valid generated ZIP cut in half.",
    ))

    # 12. Partial dictionary: one valid bank and one malformed bank
    write_zip(
        partial_dir / "OneValidOneMalformedTermBank.zip",
        {
            "index.json": to_json(term_index("JETHelper Partial Terms", "1")),
            "term_bank_1.json": to_json(valid_term_bank([
                ["猫", "ねこ", "", "n", 0, ["cat"], 1, ""],
            ])),
            "term_bank_2.json": '[["犬", "いぬ", "", "n", 0, ["dog"]]',
        },
    )
    cases.append((
        "Partial/OneValidOneMalformedTermBank.zip",
        "One readable term bank and one malformed bank; should load with warnings.",
    ))

    # 13. Partial dictionary: valid term bank, malformed optional tag bank
    write_zip(
        partial_dir / "ValidTermsMalformedTagBank.zip",
        {
            "index.json": to_json(term_index("JETHelper Partial Tag Data", "1")),
            "term_bank_1.json": to_json(valid_term_bank()),
            "tag_bank_1.json": '[["n", "partOfSpeech", 0, "noun", 0]',
        },
    )
    cases.append((
        "Partial/ValidTermsMalformedTagBank.zip",
        "Valid term data with malformed tag metadata.",
    ))

    # 14-15. Exact duplicate identity from two folders
    duplicate_files = {
        "index.json": to_json(term_index("JETHelper Exact Duplicate", "1")),
        "term_bank_1.json": to_json(valid_term_bank()),
    }
    write_zip(
        duplicate_dir / "BundledCopy" / "ExactDuplicate.zip",
        duplicate_files,
    )
    write_zip(
        duplicate_dir / "UserCopy" / "ExactDuplicate.zip",
        duplicate_files,
    )
    cases.append((
        "Duplicates/BundledCopy/ExactDuplicate.zip",
        "First exact duplicate; use as the simulated bundled copy.",
    ))
    cases.append((
        "Duplicates/UserCopy/ExactDuplicate.zip",
        "Second exact duplicate; use as the simulated user-configured copy.",
    ))

    # 16-17. Same title, different revision
    write_zip(
        duplicate_dir / "DifferentRevisions" / "Revision1.zip",
        {
            "index.json": to_json(term_index("JETHelper Revision Test", "1")),
            "term_bank_1.json": to_json(valid_term_bank([
                ["古い", "ふるい", "", "adj-i", 0, ["old"], 1, ""],
            ])),
        },
    )
    write_zip(
        duplicate_dir / "DifferentRevisions" / "Revision2.zip",
        {
            "index.json": to_json(term_index("JETHelper Revision Test", "2")),
            "term_bank_1.json": to_json(valid_term_bank([
                ["新しい", "あたらしい", "", "adj-i", 0, ["new"], 1, ""],
            ])),
        },
    )
    cases.append((
        "Duplicates/DifferentRevisions/Revision1.zip",
        "Same dictionary title, revision 1, containing 古い.",
    ))
    cases.append((
        "Duplicates/DifferentRevisions/Revision2.zip",
        "Same dictionary title, revision 2, containing 新しい.",
    ))

    return cases


def write_generated_readme(cases: list[tuple[str, str]]) -> None:
    """Write a quick guide alongside the generated archives."""
    lines = [
        "# Generated JETHelper Dictionary Test Cases",
        "",
        "These files were created by `main.py` and may be deleted at any time.",
        "Running the script again recreates the entire folder from scratch.",
        "",
        "## Test cases",
        "",
    ]

    for relative_path, description in cases:
        lines.append(f"- `{relative_path}` — {description}")

    lines.extend([
        "",
        "## Duplicate tests",
        "",
        "For the exact duplicate test:",
        "",
        "1. Treat `Duplicates/BundledCopy` as a bundled dictionary location.",
        "2. Configure `Duplicates/UserCopy` as the user dictionary folder.",
        "3. JETHelper should prefer the user-configured copy.",
        "",
        "For the revision test, point JETHelper at:",
        "",
        "`Duplicates/DifferentRevisions`",
        "",
        "Both revisions should be reported according to JETHelper's current revision policy.",
        "",
        "## Safety",
        "",
        "All entries are synthetic and tiny. They contain no third-party dictionary data.",
    ])

    (OUTPUT_DIR / "README.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    print("JETHelper Dictionary Test Generator")
    print("=" * 39)
    print()

    if OUTPUT_DIR.exists():
        print(f"Removing previous output:\n  {OUTPUT_DIR}")
        shutil.rmtree(OUTPUT_DIR)

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    cases = build_cases()
    write_generated_readme(cases)

    print()
    print(f"Created {len(cases)} test archives.")
    print(f"Output folder:\n  {OUTPUT_DIR}")
    print()
    print("Open generated/README.md for test instructions.")
    print()

    # Keeps a double-clicked console window open on Windows.
    try:
        input("Press Enter to close...")
    except EOFError:
        pass


if __name__ == "__main__":
    main()
