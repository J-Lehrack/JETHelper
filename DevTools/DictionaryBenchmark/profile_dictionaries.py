#!/usr/bin/env python3
"""Profile Yomitan dictionary archives without loading all banks into memory.

The tool accepts:
- a direct Yomitan dictionary ZIP;
- a directory containing dictionary ZIPs; or
- a container ZIP whose entries are dictionary ZIPs.

It records static archive facts that can be measured outside JETHelper:
metadata, compressed/uncompressed sizes, bank counts, and top-level row counts.
It intentionally does not reproduce JETHelper's indexing rules or estimate the
number of runtime lookup keys.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import time
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from typing import BinaryIO, Iterator, Sequence
import zipfile

TOOL_VERSION = "1.0.0"
BANK_PATTERN = re.compile(
    r"(?:^|/)(term|term_meta|kanji|kanji_meta|tag)_bank_(\d+)\.json$",
    re.IGNORECASE,
)
KNOWN_BANK_TYPES = ("term", "term_meta", "kanji", "kanji_meta", "tag")


class ProfileError(RuntimeError):
    """Raised for a profile-level input or archive problem."""


@dataclass
class BankSummary:
    files: int = 0
    rows: int = 0
    uncompressed_bytes: int = 0
    compressed_bytes: int = 0
    row_count_errors: int = 0


@dataclass
class DictionaryProfile:
    profile: str
    archive_name: str
    source: str
    container: str | None
    title: str | None
    revision: str | None
    format: int | str | None
    sequenced: bool | None
    author: str | None
    url: str | None
    description: str | None
    archive_bytes: int
    zip_entry_compressed_bytes: int
    uncompressed_bytes: int
    compression_ratio: float | None
    entry_count: int
    json_entry_count: int
    other_json_entries: int
    largest_entry_name: str | None
    largest_entry_bytes: int
    sha256: str
    banks: dict[str, BankSummary] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    elapsed_ms: float = 0.0

    @property
    def total_bank_files(self) -> int:
        return sum(bank.files for bank in self.banks.values())

    @property
    def total_rows(self) -> int:
        return sum(bank.rows for bank in self.banks.values())


@dataclass
class ProfileSummary:
    name: str
    dictionary_count: int
    archive_bytes: int
    uncompressed_bytes: int
    bank_files: int
    rows: int
    warnings: int
    errors: int


@dataclass(frozen=True)
class ProfileInput:
    name: str
    path: Path


@dataclass(frozen=True)
class DictionaryCandidate:
    profile: str
    display_name: str
    source: str
    container: str | None
    path: Path
    temporary: bool = False


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Profile Yomitan dictionary ZIP files and write JSON, CSV, and Markdown reports.",
    )
    parser.add_argument(
        "inputs",
        nargs="*",
        help="Dictionary ZIPs, container ZIPs, or directories. Each becomes a profile named after the path.",
    )
    parser.add_argument(
        "--profile",
        action="append",
        default=[],
        metavar="NAME=PATH",
        help="Add a named profile. May be repeated.",
    )
    parser.add_argument(
        "--config",
        type=Path,
        help="JSON file containing {'profiles': [{'name': '...', 'path': '...'}]}.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parent / "output",
        help="Directory for generated reports (default: DevTools/DictionaryBenchmark/output).",
    )
    parser.add_argument(
        "--report-prefix",
        default="dictionary_profile",
        help="Output filename prefix (default: dictionary_profile).",
    )
    parser.add_argument(
        "--no-sha256",
        action="store_true",
        help="Skip SHA-256 hashing when a faster inventory is preferred.",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Suppress per-dictionary progress output.",
    )
    return parser.parse_args(argv)


def load_profile_inputs(args: argparse.Namespace) -> list[ProfileInput]:
    profiles: list[ProfileInput] = []

    if args.config:
        config_path = args.config.resolve()
        try:
            config = json.loads(config_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ProfileError(f"Could not read config '{config_path}': {exc}") from exc

        raw_profiles = config.get("profiles")
        if not isinstance(raw_profiles, list):
            raise ProfileError("Config must contain a 'profiles' array.")

        for item in raw_profiles:
            if not isinstance(item, dict):
                raise ProfileError("Each config profile must be an object.")
            name = str(item.get("name", "")).strip()
            raw_path = str(item.get("path", "")).strip()
            if not name or not raw_path:
                raise ProfileError("Each config profile requires non-empty 'name' and 'path' values.")
            path = Path(raw_path)
            if not path.is_absolute():
                path = config_path.parent / path
            profiles.append(ProfileInput(name=name, path=path.resolve()))

    for spec in args.profile:
        if "=" not in spec:
            raise ProfileError(f"Invalid --profile value '{spec}'. Expected NAME=PATH.")
        name, raw_path = spec.split("=", 1)
        name = name.strip()
        raw_path = raw_path.strip().strip('"')
        if not name or not raw_path:
            raise ProfileError(f"Invalid --profile value '{spec}'. Expected NAME=PATH.")
        profiles.append(ProfileInput(name=name, path=Path(raw_path).expanduser().resolve()))

    for raw_path in args.inputs:
        path = Path(raw_path).expanduser().resolve()
        profiles.append(ProfileInput(name=path.stem, path=path))

    if not profiles:
        raise ProfileError("Provide at least one input, --profile NAME=PATH, or --config file.")

    for profile in profiles:
        if not profile.path.exists():
            raise ProfileError(f"Profile path does not exist: {profile.path}")

    return profiles


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def find_index_entry(archive: zipfile.ZipFile) -> str | None:
    candidates = [
        info.filename
        for info in archive.infolist()
        if not info.is_dir() and Path(info.filename).name.casefold() == "index.json"
    ]
    if not candidates:
        return None
    candidates.sort(key=lambda name: (name.count("/"), len(name), name.casefold()))
    return candidates[0]


def is_yomitan_archive(path: Path) -> bool:
    try:
        with zipfile.ZipFile(path) as archive:
            return find_index_entry(archive) is not None
    except (OSError, zipfile.BadZipFile):
        return False


def copy_nested_zip_to_temp(
    outer: zipfile.ZipFile,
    info: zipfile.ZipInfo,
    temp_dir: Path,
) -> Path:
    safe_name = Path(info.filename).name
    target = temp_dir / safe_name
    counter = 2
    while target.exists():
        target = temp_dir / f"{Path(safe_name).stem}_{counter}.zip"
        counter += 1
    with outer.open(info, "r") as source, target.open("wb") as destination:
        shutil.copyfileobj(source, destination, length=1024 * 1024)
    return target


def iter_dictionary_candidates(profile: ProfileInput, temp_dir: Path) -> Iterator[DictionaryCandidate]:
    path = profile.path

    if path.is_dir():
        zip_paths = sorted(
            (candidate for candidate in path.rglob("*.zip") if candidate.is_file()),
            key=lambda candidate: str(candidate).casefold(),
        )
        if not zip_paths:
            raise ProfileError(f"No ZIP files found under directory: {path}")
        for zip_path in zip_paths:
            # A directory may contain direct dictionary ZIPs, container ZIPs, or both.
            yield from iter_dictionary_candidates(
                ProfileInput(name=profile.name, path=zip_path),
                temp_dir,
            )
        return

    if path.suffix.casefold() != ".zip":
        raise ProfileError(f"Input must be a ZIP file or directory: {path}")

    if is_yomitan_archive(path):
        yield DictionaryCandidate(
            profile=profile.name,
            display_name=path.name,
            source=str(path),
            container=None,
            path=path,
        )
        return

    try:
        with zipfile.ZipFile(path) as outer:
            nested = sorted(
                (
                    info
                    for info in outer.infolist()
                    if not info.is_dir() and info.filename.casefold().endswith(".zip")
                ),
                key=lambda info: info.filename.casefold(),
            )
            if not nested:
                raise ProfileError(
                    f"'{path}' is neither a Yomitan dictionary ZIP nor a container with nested ZIPs."
                )
            for info in nested:
                nested_path = copy_nested_zip_to_temp(outer, info, temp_dir)
                yield DictionaryCandidate(
                    profile=profile.name,
                    display_name=Path(info.filename).name,
                    source=f"{path}!/{info.filename}",
                    container=str(path),
                    path=nested_path,
                    temporary=True,
                )
    except zipfile.BadZipFile as exc:
        raise ProfileError(f"Invalid ZIP archive '{path}': {exc}") from exc


def count_top_level_array_items(stream: BinaryIO) -> int:
    """Parse one JSON bank and return the number of top-level rows.

    Yomitan banks are top-level JSON arrays. The standard-library JSON parser is
    considerably faster than a Python character scanner and provides full JSON
    validation. Only one bank is retained at a time, so peak profiler memory is
    bounded by the largest individual bank rather than the whole dictionary set.
    """

    text_stream = io.TextIOWrapper(stream, encoding="utf-8-sig")
    try:
        value = json.load(text_stream)
    finally:
        # Detach so closing the wrapper does not close ZipExtFile a second time.
        try:
            text_stream.detach()
        except (ValueError, OSError):
            pass

    if not isinstance(value, list):
        raise ValueError("bank JSON root is not an array")
    return len(value)


def read_index_metadata(archive: zipfile.ZipFile, index_name: str) -> tuple[dict, list[str]]:
    warnings: list[str] = []
    try:
        with archive.open(index_name, "r") as stream:
            raw = stream.read()
        metadata = json.loads(raw.decode("utf-8-sig"))
        if not isinstance(metadata, dict):
            raise ValueError("index.json root is not an object")
        return metadata, warnings
    except (OSError, UnicodeDecodeError, json.JSONDecodeError, ValueError) as exc:
        warnings.append(f"Could not parse {index_name}: {exc}")
        return {}, warnings


def profile_dictionary(candidate: DictionaryCandidate, include_sha256: bool) -> DictionaryProfile:
    started = time.perf_counter()
    warnings: list[str] = []
    errors: list[str] = []
    banks = {bank_type: BankSummary() for bank_type in KNOWN_BANK_TYPES}
    metadata: dict = {}
    archive_bytes = candidate.path.stat().st_size
    zip_entry_compressed_bytes = 0
    uncompressed_bytes = 0
    entry_count = 0
    json_entry_count = 0
    other_json_entries = 0
    largest_entry_name: str | None = None
    largest_entry_bytes = 0

    digest = sha256_file(candidate.path) if include_sha256 else ""

    try:
        with zipfile.ZipFile(candidate.path) as archive:
            infos = [info for info in archive.infolist() if not info.is_dir()]
            entry_count = len(infos)
            zip_entry_compressed_bytes = sum(info.compress_size for info in infos)
            uncompressed_bytes = sum(info.file_size for info in infos)

            if infos:
                largest = max(infos, key=lambda info: info.file_size)
                largest_entry_name = largest.filename
                largest_entry_bytes = largest.file_size

            index_name = find_index_entry(archive)
            if index_name is None:
                errors.append("Missing index.json")
            else:
                metadata, index_warnings = read_index_metadata(archive, index_name)
                warnings.extend(index_warnings)

            for info in infos:
                if not info.filename.casefold().endswith(".json"):
                    continue
                json_entry_count += 1
                match = BANK_PATTERN.search(info.filename)
                if not match:
                    if Path(info.filename).name.casefold() != "index.json":
                        other_json_entries += 1
                    continue

                bank_type = match.group(1).casefold()
                summary = banks[bank_type]
                summary.files += 1
                summary.uncompressed_bytes += info.file_size
                summary.compressed_bytes += info.compress_size
                try:
                    with archive.open(info, "r") as stream:
                        summary.rows += count_top_level_array_items(stream)
                except (OSError, UnicodeDecodeError, ValueError, RuntimeError) as exc:
                    summary.row_count_errors += 1
                    warnings.append(f"Could not count rows in {info.filename}: {exc}")

    except (OSError, zipfile.BadZipFile, RuntimeError) as exc:
        errors.append(f"Could not inspect dictionary ZIP: {exc}")

    elapsed_ms = (time.perf_counter() - started) * 1000.0
    compression_ratio = (uncompressed_bytes / archive_bytes) if archive_bytes else None

    format_value = metadata.get("format", metadata.get("version"))
    sequenced_value = metadata.get("sequenced")
    if not isinstance(sequenced_value, bool):
        sequenced_value = None

    return DictionaryProfile(
        profile=candidate.profile,
        archive_name=candidate.display_name,
        source=candidate.source,
        container=candidate.container,
        title=_optional_text(metadata.get("title")),
        revision=_optional_text(metadata.get("revision")),
        format=format_value if isinstance(format_value, (int, str)) else None,
        sequenced=sequenced_value,
        author=_optional_text(metadata.get("author")),
        url=_optional_text(metadata.get("url")),
        description=_optional_text(metadata.get("description")),
        archive_bytes=archive_bytes,
        zip_entry_compressed_bytes=zip_entry_compressed_bytes,
        uncompressed_bytes=uncompressed_bytes,
        compression_ratio=compression_ratio,
        entry_count=entry_count,
        json_entry_count=json_entry_count,
        other_json_entries=other_json_entries,
        largest_entry_name=largest_entry_name,
        largest_entry_bytes=largest_entry_bytes,
        sha256=digest,
        banks=banks,
        warnings=warnings,
        errors=errors,
        elapsed_ms=elapsed_ms,
    )


def _optional_text(value: object) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def summarize_profiles(profiles: Sequence[DictionaryProfile]) -> list[ProfileSummary]:
    names: list[str] = []
    for profile in profiles:
        if profile.profile not in names:
            names.append(profile.profile)

    summaries: list[ProfileSummary] = []
    for name in names:
        members = [profile for profile in profiles if profile.profile == name]
        summaries.append(
            ProfileSummary(
                name=name,
                dictionary_count=len(members),
                archive_bytes=sum(member.archive_bytes for member in members),
                uncompressed_bytes=sum(member.uncompressed_bytes for member in members),
                bank_files=sum(member.total_bank_files for member in members),
                rows=sum(member.total_rows for member in members),
                warnings=sum(len(member.warnings) for member in members),
                errors=sum(len(member.errors) for member in members),
            )
        )
    return summaries


def profile_to_json(profile: DictionaryProfile) -> dict:
    data = asdict(profile)
    data["total_bank_files"] = profile.total_bank_files
    data["total_rows"] = profile.total_rows
    return data


def write_json_report(
    path: Path,
    profiles: Sequence[DictionaryProfile],
    summaries: Sequence[ProfileSummary],
) -> None:
    payload = {
        "schema_version": 1,
        "tool": "JETHelper DictionaryBenchmark",
        "tool_version": TOOL_VERSION,
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "profiles": [asdict(summary) for summary in summaries],
        "dictionaries": [profile_to_json(profile) for profile in profiles],
    }
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def write_csv_report(path: Path, profiles: Sequence[DictionaryProfile]) -> None:
    fields = [
        "profile",
        "archive_name",
        "title",
        "revision",
        "format",
        "sequenced",
        "archive_bytes",
        "uncompressed_bytes",
        "compression_ratio",
        "entry_count",
        "json_entry_count",
        "other_json_entries",
        "term_bank_files",
        "term_rows",
        "term_meta_bank_files",
        "term_meta_rows",
        "kanji_bank_files",
        "kanji_rows",
        "kanji_meta_bank_files",
        "kanji_meta_rows",
        "tag_bank_files",
        "tag_rows",
        "total_bank_files",
        "total_rows",
        "largest_entry_name",
        "largest_entry_bytes",
        "sha256",
        "elapsed_ms",
        "warning_count",
        "error_count",
        "warnings",
        "errors",
        "source",
        "container",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        for profile in profiles:
            row: dict[str, object] = {
                "profile": profile.profile,
                "archive_name": profile.archive_name,
                "title": profile.title or "",
                "revision": profile.revision or "",
                "format": profile.format if profile.format is not None else "",
                "sequenced": profile.sequenced if profile.sequenced is not None else "",
                "archive_bytes": profile.archive_bytes,
                "uncompressed_bytes": profile.uncompressed_bytes,
                "compression_ratio": (
                    f"{profile.compression_ratio:.4f}" if profile.compression_ratio is not None else ""
                ),
                "entry_count": profile.entry_count,
                "json_entry_count": profile.json_entry_count,
                "other_json_entries": profile.other_json_entries,
                "total_bank_files": profile.total_bank_files,
                "total_rows": profile.total_rows,
                "largest_entry_name": profile.largest_entry_name or "",
                "largest_entry_bytes": profile.largest_entry_bytes,
                "sha256": profile.sha256,
                "elapsed_ms": f"{profile.elapsed_ms:.2f}",
                "warning_count": len(profile.warnings),
                "error_count": len(profile.errors),
                "warnings": " | ".join(profile.warnings),
                "errors": " | ".join(profile.errors),
                "source": profile.source,
                "container": profile.container or "",
            }
            for bank_type in KNOWN_BANK_TYPES:
                summary = profile.banks[bank_type]
                row[f"{bank_type}_bank_files"] = summary.files
                row[f"{bank_type}_rows"] = summary.rows
            writer.writerow(row)


def human_bytes(value: int) -> str:
    units = ("B", "KiB", "MiB", "GiB", "TiB")
    amount = float(value)
    for unit in units:
        if amount < 1024.0 or unit == units[-1]:
            return f"{amount:.1f} {unit}" if unit != "B" else f"{int(amount)} B"
        amount /= 1024.0
    return f"{value} B"


def markdown_escape(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def write_markdown_report(
    path: Path,
    profiles: Sequence[DictionaryProfile],
    summaries: Sequence[ProfileSummary],
) -> None:
    lines = [
        "# JETHelper Dictionary Profile",
        "",
        f"Generated by DictionaryBenchmark {TOOL_VERSION} at "
        f"{datetime.now(timezone.utc).isoformat()}.",
        "",
        "This report describes archive structure and raw bank rows. It does not estimate "
        "JETHelper runtime lookup keys or managed-memory usage.",
        "",
        "## Profile totals",
        "",
        "| Profile | Dictionaries | Archive size | Uncompressed | Bank files | Rows | Warnings | Errors |",
        "|---|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for summary in summaries:
        lines.append(
            f"| {markdown_escape(summary.name)} | {summary.dictionary_count} | "
            f"{human_bytes(summary.archive_bytes)} | {human_bytes(summary.uncompressed_bytes)} | "
            f"{summary.bank_files:,} | {summary.rows:,} | {summary.warnings} | {summary.errors} |"
        )

    lines.extend(
        [
            "",
            "## Dictionaries",
            "",
            "| Profile | Archive | Dictionary title | Revision | Archive size | Uncompressed | "
            "Term rows | Term-meta rows | Kanji rows | Kanji-meta rows | Tag rows | Total rows |",
            "|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for profile in profiles:
        lines.append(
            f"| {markdown_escape(profile.profile)} | {markdown_escape(profile.archive_name)} | "
            f"{markdown_escape(profile.title or 'Unknown')} | "
            f"{markdown_escape(profile.revision or 'Unknown')} | "
            f"{human_bytes(profile.archive_bytes)} | {human_bytes(profile.uncompressed_bytes)} | "
            f"{profile.banks['term'].rows:,} | {profile.banks['term_meta'].rows:,} | "
            f"{profile.banks['kanji'].rows:,} | {profile.banks['kanji_meta'].rows:,} | "
            f"{profile.banks['tag'].rows:,} | {profile.total_rows:,} |"
        )

    issue_profiles = [profile for profile in profiles if profile.warnings or profile.errors]
    lines.extend(["", "## Warnings and errors", ""])
    if not issue_profiles:
        lines.append("No warnings or errors were recorded.")
    else:
        for profile in issue_profiles:
            lines.append(f"### {profile.archive_name}")
            lines.append("")
            for warning in profile.warnings:
                lines.append(f"- Warning: {warning}")
            for error in profile.errors:
                lines.append(f"- Error: {error}")
            lines.append("")

    lines.extend(
        [
            "",
            "## Interpretation limits",
            "",
            "- Raw bank rows are not the same as JETHelper lookup keys.",
            "- Archive sizes do not predict .NET managed-memory use by themselves.",
            "- Runtime duration, working set, private bytes, GC statistics, and atomic-replacement "
            "peak memory must be measured inside the running plugin.",
            "- The profiler reads each bank sequentially and does not retain bank contents after counting.",
            "",
        ]
    )
    path.write_text("\n".join(lines), encoding="utf-8")


def print_progress(profile: DictionaryProfile) -> None:
    issue_text = ""
    if profile.errors:
        issue_text = f", errors={len(profile.errors)}"
    elif profile.warnings:
        issue_text = f", warnings={len(profile.warnings)}"
    print(
        f"[{profile.profile}] {profile.archive_name}: "
        f"{profile.total_rows:,} rows, {human_bytes(profile.uncompressed_bytes)}, "
        f"{profile.elapsed_ms / 1000.0:.2f}s{issue_text}",
        flush=True,
    )


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    try:
        profile_inputs = load_profile_inputs(args)
    except ProfileError as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2

    profiles: list[DictionaryProfile] = []
    try:
        with tempfile.TemporaryDirectory(prefix="jethelper-dictionary-profile-") as temp_name:
            temp_dir = Path(temp_name)
            for profile_input in profile_inputs:
                try:
                    candidates = iter_dictionary_candidates(profile_input, temp_dir)
                    for candidate in candidates:
                        result = profile_dictionary(candidate, include_sha256=not args.no_sha256)
                        profiles.append(result)
                        if not args.quiet:
                            print_progress(result)
                except ProfileError as exc:
                    print(f"Error: {exc}", file=sys.stderr)
                    return 2
    except KeyboardInterrupt:
        print("\nCancelled by user.", file=sys.stderr)
        return 130

    if not profiles:
        print("Error: no dictionary archives were profiled.", file=sys.stderr)
        return 2

    args.output_dir.mkdir(parents=True, exist_ok=True)
    summaries = summarize_profiles(profiles)
    json_path = args.output_dir / f"{args.report_prefix}.json"
    csv_path = args.output_dir / f"{args.report_prefix}.csv"
    markdown_path = args.output_dir / f"{args.report_prefix}.md"

    write_json_report(json_path, profiles, summaries)
    write_csv_report(csv_path, profiles)
    write_markdown_report(markdown_path, profiles, summaries)

    print("\nReports written:")
    print(f"  {json_path}")
    print(f"  {csv_path}")
    print(f"  {markdown_path}")

    return 1 if any(profile.errors for profile in profiles) else 0


if __name__ == "__main__":
    raise SystemExit(main())
