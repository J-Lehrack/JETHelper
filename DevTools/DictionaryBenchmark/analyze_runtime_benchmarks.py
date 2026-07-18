#!/usr/bin/env python3
"""Analyze JETHelper dictionary runtime benchmark JSONL logs.

This development-only tool summarizes the structured events written by
JETHelper.dictionary-benchmark.jsonl. It validates each run's lifecycle,
reconciles loader totals, separates startup and replacement scenarios, and
writes JSON, CSV, and Markdown reports without third-party packages.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import Counter, defaultdict
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from statistics import median
import sys
from typing import Any, Iterable, Iterator, Sequence

TOOL_VERSION = "1.0.0"
SUCCESS_OUTCOMES = {"ready", "ready_with_warnings"}
TERMINAL_EVENT_TYPES = {"run_completed", "run_aborted"}


class AnalysisError(RuntimeError):
    """Raised for an input or report-generation problem."""


@dataclass(frozen=True)
class DatasetInput:
    name: str
    path: Path


@dataclass
class RunSummary:
    dataset: str
    source_file: str
    run_id: str
    profile: str
    workload_id: str
    trigger: str
    scenario: str
    schema_version: int | None
    started_at: str | None
    finished_at: str | None
    reload_generation: int | None
    had_active_snapshot: bool | None
    outcome: str
    terminal_event: str | None
    duration_ms: float | None
    discovery_ms: float | None
    validation_ms: float | None
    indexing_ms: float | None
    validation_source_count: int
    indexing_source_count: int
    banks_processed: int | None
    rows_processed: int | None
    lookup_key_count: int | None
    stored_result_object_count: int | None
    baseline_managed_bytes: int | None
    preactivation_managed_bytes: int | None
    activation_managed_bytes: int | None
    terminal_managed_bytes: int | None
    peak_managed_bytes: int | None
    peak_working_set_bytes: int | None
    peak_private_memory_bytes: int | None
    post_collection_managed_bytes: int | None
    old_snapshot_alive_after_collection: bool | None
    event_count: int
    event_types: dict[str, int] = field(default_factory=dict)
    issues: list[str] = field(default_factory=list)

    @property
    def is_success(self) -> bool:
        return self.outcome in SUCCESS_OUTCOMES

    @property
    def managed_growth_to_preactivation_bytes(self) -> int | None:
        if self.baseline_managed_bytes is None or self.preactivation_managed_bytes is None:
            return None
        return self.preactivation_managed_bytes - self.baseline_managed_bytes


@dataclass
class AggregateSummary:
    dataset: str
    workload_id: str
    profile_labels: str
    scenario: str
    trigger: str
    run_count: int
    successful_runs: int
    cancelled_runs: int
    failed_runs: int
    aborted_runs: int
    runs_with_issues: int
    duration_min_ms: float | None
    duration_median_ms: float | None
    duration_max_ms: float | None
    discovery_median_ms: float | None
    validation_median_ms: float | None
    indexing_median_ms: float | None
    rows_processed: int | None
    lookup_key_count: int | None
    stored_result_object_count: int | None
    baseline_managed_median_bytes: float | None
    preactivation_managed_median_bytes: float | None
    managed_growth_to_preactivation_median_bytes: float | None
    peak_managed_median_bytes: float | None
    post_collection_managed_median_bytes: float | None


@dataclass
class LoadedDataset:
    dataset: DatasetInput
    records: list[dict[str, Any]]
    warnings: list[str]


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Analyze JETHelper.dictionary-benchmark.jsonl files and write "
            "run, profile, lifecycle, duration, and memory summaries."
        )
    )
    parser.add_argument(
        "inputs",
        nargs="*",
        help=(
            "Benchmark JSONL files. Each positional file uses its filename stem "
            "as the dataset label."
        ),
    )
    parser.add_argument(
        "--dataset",
        action="append",
        default=[],
        metavar="NAME=PATH",
        help=(
            "Add a named dataset, such as --dataset desktop=JETHelper.dictionary-"
            "benchmark.jsonl. May be repeated."
        ),
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parent / "output",
        help="Directory for generated reports (default: DevTools/DictionaryBenchmark/output).",
    )
    parser.add_argument(
        "--report-prefix",
        default="runtime_benchmark_summary",
        help="Output filename prefix (default: runtime_benchmark_summary).",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Suppress console summary output.",
    )
    return parser.parse_args(argv)


def parse_dataset_argument(raw: str) -> DatasetInput:
    if "=" not in raw:
        raise AnalysisError(f"Named dataset must use NAME=PATH: {raw}")
    name, raw_path = raw.split("=", 1)
    name = name.strip()
    path = Path(raw_path.strip()).expanduser()
    if not name:
        raise AnalysisError(f"Dataset name cannot be empty: {raw}")
    return DatasetInput(name=name, path=path)


def load_dataset_inputs(args: argparse.Namespace) -> list[DatasetInput]:
    datasets: list[DatasetInput] = []
    for raw in args.dataset:
        datasets.append(parse_dataset_argument(raw))
    for raw_path in args.inputs:
        path = Path(raw_path).expanduser()
        datasets.append(DatasetInput(name=path.stem, path=path))

    if not datasets:
        raise AnalysisError("Provide at least one benchmark JSONL file.")

    seen: Counter[str] = Counter()
    unique: list[DatasetInput] = []
    for item in datasets:
        path = item.path.resolve()
        if not path.is_file():
            raise AnalysisError(f"Benchmark log does not exist: {path}")
        seen[item.name] += 1
        name = item.name if seen[item.name] == 1 else f"{item.name}-{seen[item.name]}"
        unique.append(DatasetInput(name=name, path=path))
    return unique


def load_json_records(dataset: DatasetInput) -> LoadedDataset:
    try:
        text = dataset.path.read_text(encoding="utf-8-sig")
    except OSError as exc:
        raise AnalysisError(f"Could not read '{dataset.path}': {exc}") from exc

    warnings: list[str] = []
    records: list[dict[str, Any]] = []
    line_errors: list[str] = []

    for line_number, line in enumerate(text.splitlines(), start=1):
        if not line.strip():
            continue
        try:
            value = json.loads(line)
        except json.JSONDecodeError as exc:
            line_errors.append(f"line {line_number}: {exc.msg}")
            continue
        if not isinstance(value, dict):
            line_errors.append(f"line {line_number}: top-level JSON value is not an object")
            continue
        records.append(value)

    if line_errors:
        # A prior editor may have pretty-printed one or more objects. Fall back
        # to decoding a whitespace-separated JSON stream before rejecting it.
        stream_records = list(decode_json_stream(text, dataset.path))
        records = stream_records
        warnings.append(
            "Input was not strict one-object-per-line JSONL; the analyzer used "
            "whitespace-separated JSON stream recovery. Preserve future logs "
            "without formatting them in an editor."
        )

    if not records:
        raise AnalysisError(f"No benchmark event objects were found in '{dataset.path}'.")

    return LoadedDataset(dataset=dataset, records=records, warnings=warnings)


def decode_json_stream(text: str, path: Path) -> Iterator[dict[str, Any]]:
    decoder = json.JSONDecoder()
    index = 0
    length = len(text)
    while index < length:
        while index < length and text[index].isspace():
            index += 1
        if index >= length:
            break
        try:
            value, end = decoder.raw_decode(text, index)
        except json.JSONDecodeError as exc:
            raise AnalysisError(
                f"Could not parse '{path}' near character {exc.pos}: {exc.msg}"
            ) from exc
        if not isinstance(value, dict):
            raise AnalysisError(
                f"Benchmark stream '{path}' contains a non-object JSON value near character {index}."
            )
        yield value
        index = end


def first_event(events: list[dict[str, Any]], event_type: str) -> dict[str, Any] | None:
    return next((event for event in events if event.get("event_type") == event_type), None)


def event_data(event: dict[str, Any] | None) -> dict[str, Any]:
    if event is None:
        return {}
    data = event.get("data")
    return data if isinstance(data, dict) else {}


def nested_int(mapping: dict[str, Any], *keys: str) -> int | None:
    value: Any = mapping
    for key in keys:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def nested_float(mapping: dict[str, Any], *keys: str) -> float | None:
    value: Any = mapping
    for key in keys:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return float(value)
    return None


def nested_bool(mapping: dict[str, Any], *keys: str) -> bool | None:
    value: Any = mapping
    for key in keys:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    return value if isinstance(value, bool) else None


def determine_scenario(trigger: str, had_active_snapshot: bool | None) -> str:
    if trigger == "plugin-startup" and had_active_snapshot is False:
        return "plugin-startup/no-active-snapshot"
    if had_active_snapshot is True:
        return "replacement/active-snapshot"
    if had_active_snapshot is False:
        return "reload/no-active-snapshot"
    return "unknown"


def timestamp_key(event: dict[str, Any]) -> tuple[str, int]:
    timestamp = event.get("timestamp")
    return (timestamp if isinstance(timestamp, str) else "", int(event.get("_record_index", 0)))


def summarize_loaded_dataset(loaded: LoadedDataset) -> list[RunSummary]:
    records_by_run: dict[str, list[dict[str, Any]]] = defaultdict(list)
    orphan_count = 0

    for index, record in enumerate(loaded.records):
        record = dict(record)
        record["_record_index"] = index
        run_id = record.get("run_id")
        if not isinstance(run_id, str) or not run_id.strip():
            orphan_count += 1
            continue
        records_by_run[run_id].append(record)

    if orphan_count:
        loaded.warnings.append(f"Ignored {orphan_count} record(s) without a run_id.")

    summaries: list[RunSummary] = []
    for run_id, events in records_by_run.items():
        events.sort(key=timestamp_key)
        summaries.append(summarize_run(loaded.dataset, run_id, events))

    summaries.sort(key=lambda run: (run.started_at or "", run.run_id))
    return summaries


def summarize_run(
    dataset: DatasetInput,
    run_id: str,
    events: list[dict[str, Any]],
) -> RunSummary:
    counts = Counter(
        event.get("event_type") for event in events if isinstance(event.get("event_type"), str)
    )
    run_started = first_event(events, "run_started")
    reload_started = first_event(events, "reload_started")
    discovery = first_event(events, "discovery_completed")
    validation = first_event(events, "validation_completed")
    indexing = first_event(events, "indexing_completed")
    activation = first_event(events, "snapshot_activated")
    peak = first_event(events, "memory_peak")
    completed = first_event(events, "run_completed")
    aborted = first_event(events, "run_aborted")
    post_collection = first_event(events, "post_collection_memory")

    run_started_data = event_data(run_started)
    reload_started_data = event_data(reload_started)
    discovery_data = event_data(discovery)
    validation_data = event_data(validation)
    indexing_data = event_data(indexing)
    activation_data = event_data(activation)
    peak_data = event_data(peak)
    completed_data = event_data(completed)
    aborted_data = event_data(aborted)
    post_collection_data = event_data(post_collection)

    profile_values = {
        event.get("profile") for event in events if isinstance(event.get("profile"), str)
    }
    trigger_values = {
        event.get("trigger") for event in events if isinstance(event.get("trigger"), str)
    }
    profile = next(iter(profile_values), "unknown-profile")
    trigger = next(iter(trigger_values), "unknown-trigger")
    had_active_snapshot = nested_bool(reload_started_data, "had_active_snapshot")

    terminal_event = "run_completed" if completed is not None else "run_aborted" if aborted is not None else None
    outcome_value = completed_data.get("outcome") if completed is not None else aborted_data.get("outcome")
    outcome = outcome_value if isinstance(outcome_value, str) else "missing-terminal"

    generations = {
        value
        for event in events
        for value in [event.get("reload_generation")]
        if isinstance(value, int) and not isinstance(value, bool)
    }
    reload_generation = next(iter(generations), None)

    issues = validate_run(
        events=events,
        counts=counts,
        profile_values=profile_values,
        trigger_values=trigger_values,
        generations=generations,
        outcome=outcome,
        indexing_data=indexing_data,
    )

    started_at = run_started.get("timestamp") if run_started else events[0].get("timestamp")
    terminal = completed or aborted
    finished_at = terminal.get("timestamp") if terminal else None

    duration_ms = nested_float(completed_data, "duration_ms")
    if duration_ms is None and terminal is not None:
        duration_ms = elapsed_from_timestamps(started_at, finished_at)

    validation_sources = [event for event in events if event.get("event_type") == "validation_source"]
    indexing_sources = [event for event in events if event.get("event_type") == "indexing_source"]
    workload_id = build_workload_id(indexing_sources, indexing_data, profile)

    schema_values = {
        event.get("schema_version")
        for event in events
        if isinstance(event.get("schema_version"), int)
    }
    schema_version = next(iter(schema_values), None)
    if len(schema_values) > 1:
        issues.append(f"Run contains multiple schema versions: {sorted(schema_values)}.")

    return RunSummary(
        dataset=dataset.name,
        source_file=str(dataset.path),
        run_id=run_id,
        profile=profile,
        workload_id=workload_id,
        trigger=trigger,
        scenario=determine_scenario(trigger, had_active_snapshot),
        schema_version=schema_version,
        started_at=started_at if isinstance(started_at, str) else None,
        finished_at=finished_at if isinstance(finished_at, str) else None,
        reload_generation=reload_generation,
        had_active_snapshot=had_active_snapshot,
        outcome=outcome,
        terminal_event=terminal_event,
        duration_ms=duration_ms,
        discovery_ms=nested_float(discovery_data, "duration_ms"),
        validation_ms=nested_float(validation_data, "duration_ms"),
        indexing_ms=nested_float(indexing_data, "duration_ms"),
        validation_source_count=len(validation_sources),
        indexing_source_count=len(indexing_sources),
        banks_processed=nested_int(indexing_data, "banks_processed"),
        rows_processed=nested_int(indexing_data, "rows_processed"),
        lookup_key_count=nested_int(indexing_data, "lookup_key_count"),
        stored_result_object_count=nested_int(indexing_data, "stored_result_object_count"),
        baseline_managed_bytes=nested_int(run_started_data, "memory", "managed_bytes"),
        preactivation_managed_bytes=nested_int(indexing_data, "memory", "managed_bytes"),
        activation_managed_bytes=nested_int(activation_data, "memory", "managed_bytes"),
        terminal_managed_bytes=nested_int(completed_data, "terminal_memory", "managed_bytes"),
        peak_managed_bytes=nested_int(peak_data, "peak", "managed_bytes"),
        peak_working_set_bytes=nested_int(peak_data, "peak", "working_set_bytes"),
        peak_private_memory_bytes=nested_int(peak_data, "peak", "private_memory_bytes"),
        post_collection_managed_bytes=nested_int(post_collection_data, "memory", "managed_bytes"),
        old_snapshot_alive_after_collection=nested_bool(post_collection_data, "old_snapshot_alive"),
        event_count=len(events),
        event_types=dict(sorted(counts.items())),
        issues=issues,
    )


def validate_run(
    *,
    events: list[dict[str, Any]],
    counts: Counter[str],
    profile_values: set[str],
    trigger_values: set[str],
    generations: set[int],
    outcome: str,
    indexing_data: dict[str, Any],
) -> list[str]:
    issues: list[str] = []

    if len(profile_values) != 1:
        issues.append(f"Expected one profile label, found {sorted(profile_values)}.")
    if len(trigger_values) != 1:
        issues.append(f"Expected one trigger label, found {sorted(trigger_values)}.")
    if len(generations) > 1:
        issues.append(f"Expected one reload generation, found {sorted(generations)}.")

    require_exactly_one(counts, "run_started", issues)
    require_exactly_one(counts, "reload_started", issues)

    terminal_count = sum(counts[event_type] for event_type in TERMINAL_EVENT_TYPES)
    if terminal_count != 1:
        issues.append(f"Expected exactly one terminal event, found {terminal_count}.")

    if outcome in SUCCESS_OUTCOMES:
        for event_type in (
            "discovery_completed",
            "validation_completed",
            "indexing_completed",
            "snapshot_activated",
            "memory_peak",
            "run_completed",
        ):
            require_exactly_one(counts, event_type, issues)
        validate_order(
            events,
            [
                "run_started",
                "reload_started",
                "discovery_completed",
                "validation_completed",
                "indexing_completed",
                "snapshot_activated",
                "memory_peak",
                "run_completed",
            ],
            issues,
        )
        reconcile_indexing_totals(events, indexing_data, issues)
    elif outcome in {"cancelled", "failed"}:
        require_exactly_one(counts, "memory_peak", issues)
        require_exactly_one(counts, "run_completed", issues)
        if counts["snapshot_activated"]:
            issues.append(f"Outcome '{outcome}' unexpectedly activated a snapshot.")
    elif outcome == "plugin_disposed":
        require_exactly_one(counts, "run_aborted", issues)
        if counts["run_completed"]:
            issues.append("Plugin-disposal run also contains run_completed.")
    elif outcome == "missing-terminal":
        issues.append("Run has no terminal outcome.")

    terminal_indexes = [
        index
        for index, event in enumerate(events)
        if event.get("event_type") in TERMINAL_EVENT_TYPES
    ]
    if terminal_indexes:
        last_terminal = terminal_indexes[-1]
        for event in events[last_terminal + 1 :]:
            if event.get("event_type") != "post_collection_memory":
                issues.append(
                    f"Event '{event.get('event_type')}' appears after the terminal outcome."
                )

    return issues


def require_exactly_one(counts: Counter[str], event_type: str, issues: list[str]) -> None:
    count = counts[event_type]
    if count != 1:
        issues.append(f"Expected exactly one {event_type} event, found {count}.")


def validate_order(
    events: list[dict[str, Any]],
    expected: list[str],
    issues: list[str],
) -> None:
    indexes: list[int] = []
    for event_type in expected:
        index = next(
            (i for i, event in enumerate(events) if event.get("event_type") == event_type),
            None,
        )
        if index is None:
            return
        indexes.append(index)
    if indexes != sorted(indexes):
        issues.append("Required success events are out of lifecycle order.")


def reconcile_indexing_totals(
    events: list[dict[str, Any]],
    indexing_data: dict[str, Any],
    issues: list[str],
) -> None:
    source_data = [
        event_data(event) for event in events if event.get("event_type") == "indexing_source"
    ]
    if not source_data:
        issues.append("Successful run contains no indexing_source records.")
        return

    fields = (
        ("banks_processed", "banks_processed"),
        ("rows_processed", "rows_processed"),
        ("lookup_keys_added", "lookup_key_count"),
        ("stored_result_objects_added", "stored_result_object_count"),
    )
    for source_field, total_field in fields:
        source_values = [data.get(source_field) for data in source_data]
        if not all(isinstance(value, int) and not isinstance(value, bool) for value in source_values):
            issues.append(f"One or more indexing_source records lack integer {source_field}.")
            continue
        actual = sum(source_values)
        expected = indexing_data.get(total_field)
        if not isinstance(expected, int) or isinstance(expected, bool):
            issues.append(f"indexing_completed lacks integer {total_field}.")
        elif actual != expected:
            issues.append(
                f"Indexing total mismatch for {total_field}: sources={actual}, total={expected}."
            )

    service_data = [
        event_data(event)
        for event in events
        if event.get("event_type") == "indexing_service_summary"
    ]
    if service_data:
        comparisons = (
            ("banks_processed", "banks_processed"),
            ("rows_processed", "rows_processed"),
            ("lookup_key_count", "lookup_key_count"),
            ("stored_result_object_count", "stored_result_object_count"),
        )
        for service_field, total_field in comparisons:
            values = [data.get(service_field) for data in service_data]
            if all(isinstance(value, int) and not isinstance(value, bool) for value in values):
                expected = indexing_data.get(total_field)
                if isinstance(expected, int) and sum(values) != expected:
                    issues.append(
                        f"Service-summary mismatch for {total_field}: "
                        f"services={sum(values)}, total={expected}."
                    )


def build_workload_id(
    indexing_sources: list[dict[str, Any]],
    indexing_data: dict[str, Any],
    profile: str,
) -> str:
    """Create a stable workload fingerprint from actual indexed source metrics.

    Benchmark labels are intentionally user supplied and may be unique per run.
    Grouping by the real source/count signature allows repeated runs and machines
    to be compared even when labels contain suffixes such as `-test-1`.
    """
    signature_rows: list[tuple[Any, ...]] = []
    for event in indexing_sources:
        data = event_data(event)
        signature_rows.append(
            (
                data.get("service_name"),
                data.get("source_name"),
                data.get("banks_processed"),
                data.get("rows_processed"),
                data.get("lookup_keys_added"),
                data.get("stored_result_objects_added"),
            )
        )

    if not signature_rows:
        return "unindexed-" + hashlib.sha256(profile.encode("utf-8")).hexdigest()[:10]

    payload = {
        "sources": sorted(signature_rows, key=lambda row: tuple(str(value) for value in row)),
        "totals": {
            "banks": indexing_data.get("banks_processed"),
            "rows": indexing_data.get("rows_processed"),
            "keys": indexing_data.get("lookup_key_count"),
            "objects": indexing_data.get("stored_result_object_count"),
        },
    }
    encoded = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return "workload-" + hashlib.sha256(encoded.encode("utf-8")).hexdigest()[:10]


def elapsed_from_timestamps(start: Any, finish: Any) -> float | None:
    if not isinstance(start, str) or not isinstance(finish, str):
        return None
    try:
        start_dt = datetime.fromisoformat(start)
        finish_dt = datetime.fromisoformat(finish)
    except ValueError:
        return None
    return (finish_dt - start_dt).total_seconds() * 1000.0


def median_or_none(values: Iterable[int | float | None]) -> float | None:
    present = [float(value) for value in values if value is not None and math.isfinite(float(value))]
    return median(present) if present else None


def min_or_none(values: Iterable[int | float | None]) -> float | None:
    present = [float(value) for value in values if value is not None and math.isfinite(float(value))]
    return min(present) if present else None


def max_or_none(values: Iterable[int | float | None]) -> float | None:
    present = [float(value) for value in values if value is not None and math.isfinite(float(value))]
    return max(present) if present else None


def stable_int_or_none(values: Iterable[int | None]) -> int | None:
    present = [value for value in values if value is not None]
    if not present:
        return None
    counts = Counter(present)
    return counts.most_common(1)[0][0]


def aggregate_runs(runs: list[RunSummary]) -> list[AggregateSummary]:
    groups: dict[tuple[str, str, str, str], list[RunSummary]] = defaultdict(list)
    for run in runs:
        groups[(run.dataset, run.workload_id, run.scenario, run.trigger)].append(run)

    aggregates: list[AggregateSummary] = []
    for (dataset, workload_id, scenario, trigger), group in sorted(groups.items()):
        successful = [run for run in group if run.is_success]
        profile_labels = ", ".join(sorted({run.profile for run in group}))
        aggregates.append(
            AggregateSummary(
                dataset=dataset,
                workload_id=workload_id,
                profile_labels=profile_labels,
                scenario=scenario,
                trigger=trigger,
                run_count=len(group),
                successful_runs=len(successful),
                cancelled_runs=sum(run.outcome == "cancelled" for run in group),
                failed_runs=sum(run.outcome == "failed" for run in group),
                aborted_runs=sum(run.terminal_event == "run_aborted" for run in group),
                runs_with_issues=sum(bool(run.issues) for run in group),
                duration_min_ms=min_or_none(run.duration_ms for run in successful),
                duration_median_ms=median_or_none(run.duration_ms for run in successful),
                duration_max_ms=max_or_none(run.duration_ms for run in successful),
                discovery_median_ms=median_or_none(run.discovery_ms for run in successful),
                validation_median_ms=median_or_none(run.validation_ms for run in successful),
                indexing_median_ms=median_or_none(run.indexing_ms for run in successful),
                rows_processed=stable_int_or_none(run.rows_processed for run in successful),
                lookup_key_count=stable_int_or_none(run.lookup_key_count for run in successful),
                stored_result_object_count=stable_int_or_none(
                    run.stored_result_object_count for run in successful
                ),
                baseline_managed_median_bytes=median_or_none(
                    run.baseline_managed_bytes for run in successful
                ),
                preactivation_managed_median_bytes=median_or_none(
                    run.preactivation_managed_bytes for run in successful
                ),
                managed_growth_to_preactivation_median_bytes=median_or_none(
                    run.managed_growth_to_preactivation_bytes for run in successful
                ),
                peak_managed_median_bytes=median_or_none(
                    run.peak_managed_bytes for run in successful
                ),
                post_collection_managed_median_bytes=median_or_none(
                    run.post_collection_managed_bytes for run in successful
                ),
            )
        )
    return aggregates


def write_reports(
    output_dir: Path,
    prefix: str,
    loaded_datasets: list[LoadedDataset],
    runs: list[RunSummary],
    aggregates: list[AggregateSummary],
) -> list[Path]:
    try:
        output_dir.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise AnalysisError(f"Could not create output directory '{output_dir}': {exc}") from exc

    json_path = output_dir / f"{prefix}.json"
    runs_csv_path = output_dir / f"{prefix}_runs.csv"
    profiles_csv_path = output_dir / f"{prefix}_profiles.csv"
    markdown_path = output_dir / f"{prefix}.md"

    payload = {
        "tool": {
            "name": "JETHelper runtime benchmark analyzer",
            "version": TOOL_VERSION,
            "generated_at": datetime.now(timezone.utc).isoformat(),
        },
        "inputs": [
            {
                "dataset": loaded.dataset.name,
                "path": str(loaded.dataset.path),
                "record_count": len(loaded.records),
                "warnings": loaded.warnings,
            }
            for loaded in loaded_datasets
        ],
        "summary": {
            "run_count": len(runs),
            "successful_runs": sum(run.is_success for run in runs),
            "cancelled_runs": sum(run.outcome == "cancelled" for run in runs),
            "failed_runs": sum(run.outcome == "failed" for run in runs),
            "aborted_runs": sum(run.terminal_event == "run_aborted" for run in runs),
            "runs_with_issues": sum(bool(run.issues) for run in runs),
        },
        "aggregates": [asdict(item) for item in aggregates],
        "runs": [run_to_json(run) for run in runs],
    }

    try:
        json_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        write_runs_csv(runs_csv_path, runs)
        write_profiles_csv(profiles_csv_path, aggregates)
        markdown_path.write_text(
            build_markdown(loaded_datasets, runs, aggregates),
            encoding="utf-8",
        )
    except OSError as exc:
        raise AnalysisError(f"Could not write reports: {exc}") from exc

    return [json_path, runs_csv_path, profiles_csv_path, markdown_path]


def run_to_json(run: RunSummary) -> dict[str, Any]:
    data = asdict(run)
    data["managed_growth_to_preactivation_bytes"] = (
        run.managed_growth_to_preactivation_bytes
    )
    data["is_success"] = run.is_success
    return data


def write_runs_csv(path: Path, runs: list[RunSummary]) -> None:
    fieldnames = [
        "dataset",
        "source_file",
        "run_id",
        "profile",
        "workload_id",
        "trigger",
        "scenario",
        "started_at",
        "finished_at",
        "reload_generation",
        "had_active_snapshot",
        "outcome",
        "terminal_event",
        "duration_ms",
        "discovery_ms",
        "validation_ms",
        "indexing_ms",
        "validation_source_count",
        "indexing_source_count",
        "banks_processed",
        "rows_processed",
        "lookup_key_count",
        "stored_result_object_count",
        "baseline_managed_bytes",
        "preactivation_managed_bytes",
        "managed_growth_to_preactivation_bytes",
        "activation_managed_bytes",
        "terminal_managed_bytes",
        "peak_managed_bytes",
        "peak_working_set_bytes",
        "peak_private_memory_bytes",
        "post_collection_managed_bytes",
        "old_snapshot_alive_after_collection",
        "event_count",
        "issue_count",
        "issues",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        for run in runs:
            row = {name: getattr(run, name, None) for name in fieldnames}
            row["managed_growth_to_preactivation_bytes"] = (
                run.managed_growth_to_preactivation_bytes
            )
            row["issue_count"] = len(run.issues)
            row["issues"] = " | ".join(run.issues)
            writer.writerow(row)


def write_profiles_csv(path: Path, aggregates: list[AggregateSummary]) -> None:
    fieldnames = list(asdict(aggregates[0]).keys()) if aggregates else [
        "dataset",
        "workload_id",
        "profile_labels",
        "scenario",
        "trigger",
        "run_count",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        for aggregate in aggregates:
            writer.writerow(asdict(aggregate))


def build_markdown(
    loaded_datasets: list[LoadedDataset],
    runs: list[RunSummary],
    aggregates: list[AggregateSummary],
) -> str:
    lines: list[str] = []
    lines.append("# JETHelper Runtime Dictionary Benchmark Summary")
    lines.append("")
    lines.append(
        f"Generated by runtime benchmark analyzer {TOOL_VERSION} on "
        f"{datetime.now(timezone.utc).isoformat()}."
    )
    lines.append("")
    lines.append("> Memory measurements describe the entire FFXIV process, including Dalamud and other plugins. Compare repeated runs under stable conditions; do not interpret them as exact JETHelper-only allocations.")
    lines.append("")
    lines.append("## Overview")
    lines.append("")
    lines.append(f"- Input datasets: {len(loaded_datasets)}")
    lines.append(f"- Runs: {len(runs)}")
    lines.append(f"- Successful: {sum(run.is_success for run in runs)}")
    lines.append(f"- Cancelled: {sum(run.outcome == 'cancelled' for run in runs)}")
    lines.append(f"- Failed: {sum(run.outcome == 'failed' for run in runs)}")
    lines.append(f"- Aborted: {sum(run.terminal_event == 'run_aborted' for run in runs)}")
    lines.append(f"- Runs with validation issues: {sum(bool(run.issues) for run in runs)}")
    lines.append("")

    input_warnings = [
        f"**{loaded.dataset.name}:** {warning}"
        for loaded in loaded_datasets
        for warning in loaded.warnings
    ]
    if input_warnings:
        lines.append("## Input warnings")
        lines.append("")
        lines.extend(f"- {warning}" for warning in input_warnings)
        lines.append("")

    lines.append("## Profile and scenario aggregates")
    lines.append("")
    lines.append(
        "| Dataset | Workload | Profile labels | Scenario | Runs | Outcomes | Duration min / median / max | Validation median | Indexing median | Rows | Keys | Managed growth to preactivation |"
    )
    lines.append("|---|---|---|---|---:|---|---:|---:|---:|---:|---:|---:|")
    for item in aggregates:
        outcomes = (
            f"ready={item.successful_runs}, cancelled={item.cancelled_runs}, "
            f"failed={item.failed_runs}, aborted={item.aborted_runs}"
        )
        duration = " / ".join(
            format_ms(value)
            for value in (
                item.duration_min_ms,
                item.duration_median_ms,
                item.duration_max_ms,
            )
        )
        lines.append(
            "| "
            + " | ".join(
                [
                    escape_markdown(item.dataset),
                    escape_markdown(item.workload_id),
                    escape_markdown(item.profile_labels),
                    escape_markdown(item.scenario),
                    str(item.run_count),
                    outcomes,
                    duration,
                    format_ms(item.validation_median_ms),
                    format_ms(item.indexing_median_ms),
                    format_int(item.rows_processed),
                    format_int(item.lookup_key_count),
                    format_bytes(item.managed_growth_to_preactivation_median_bytes),
                ]
            )
            + " |"
        )
    lines.append("")

    lines.append("## Individual runs")
    lines.append("")
    lines.append(
        "| Dataset | Profile | Workload | Scenario | Outcome | Duration | Validation | Indexing | Rows | Keys | Baseline managed | Before activation | Peak managed | Issues |"
    )
    lines.append("|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
    for run in runs:
        lines.append(
            "| "
            + " | ".join(
                [
                    escape_markdown(run.dataset),
                    escape_markdown(run.profile),
                    escape_markdown(run.workload_id),
                    escape_markdown(run.scenario),
                    escape_markdown(run.outcome),
                    format_ms(run.duration_ms),
                    format_ms(run.validation_ms),
                    format_ms(run.indexing_ms),
                    format_int(run.rows_processed),
                    format_int(run.lookup_key_count),
                    format_bytes(run.baseline_managed_bytes),
                    format_bytes(run.preactivation_managed_bytes),
                    format_bytes(run.peak_managed_bytes),
                    str(len(run.issues)),
                ]
            )
            + " |"
        )
    lines.append("")

    problematic = [run for run in runs if run.issues]
    lines.append("## Lifecycle and reconciliation findings")
    lines.append("")
    if not problematic:
        lines.append("No lifecycle, terminal-outcome, ordering, or loader-total inconsistencies were detected.")
    else:
        for run in problematic:
            lines.append(
                f"### {escape_markdown(run.dataset)} / {escape_markdown(run.profile)} / `{run.run_id}`"
            )
            lines.append("")
            lines.extend(f"- {issue}" for issue in run.issues)
            lines.append("")

    lines.append("## Interpretation notes")
    lines.append("")
    lines.append("- `plugin-startup/no-active-snapshot` separates startup runs from replacements that retain an old active snapshot.")
    lines.append("- `managed growth to preactivation` is the run-baseline managed value subtracted from the after-indexing/before-activation measurement.")
    lines.append("- Profile labels are user supplied. The analyzer also creates a workload fingerprint from actual indexed sources and counts, so uniquely named repeated runs can still be aggregated.")
    lines.append("- Exact dictionary paths remain in the original JSONL but are intentionally omitted from the Markdown tables.")
    lines.append("")
    return "\n".join(lines)


def escape_markdown(value: Any) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def format_ms(value: float | None) -> str:
    if value is None:
        return "—"
    if value >= 1000:
        return f"{value / 1000.0:.2f} s"
    return f"{value:.1f} ms"


def format_int(value: int | None) -> str:
    return "—" if value is None else f"{value:,}"


def format_bytes(value: int | float | None) -> str:
    if value is None:
        return "—"
    size = float(value)
    sign = "-" if size < 0 else ""
    size = abs(size)
    units = ("B", "KiB", "MiB", "GiB", "TiB")
    unit = units[0]
    for candidate in units:
        unit = candidate
        if size < 1024.0 or candidate == units[-1]:
            break
        size /= 1024.0
    return f"{sign}{size:.2f} {unit}"


def print_console_summary(
    loaded_datasets: list[LoadedDataset],
    runs: list[RunSummary],
    aggregates: list[AggregateSummary],
    paths: list[Path],
) -> None:
    print(f"Loaded {sum(len(item.records) for item in loaded_datasets):,} events from {len(loaded_datasets)} dataset(s).")
    print(
        f"Runs: {len(runs)}; successful={sum(run.is_success for run in runs)}; "
        f"cancelled={sum(run.outcome == 'cancelled' for run in runs)}; "
        f"failed={sum(run.outcome == 'failed' for run in runs)}; "
        f"aborted={sum(run.terminal_event == 'run_aborted' for run in runs)}; "
        f"with issues={sum(bool(run.issues) for run in runs)}."
    )
    print("")
    for aggregate in aggregates:
        print(
            f"[{aggregate.dataset}] {aggregate.workload_id} / {aggregate.scenario}: "
            f"runs={aggregate.run_count}, median={format_ms(aggregate.duration_median_ms)}, "
            f"rows={format_int(aggregate.rows_processed)}, "
            f"keys={format_int(aggregate.lookup_key_count)}"
        )
    print("")
    print("Reports:")
    for path in paths:
        print(f"  {path}")


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv if argv is not None else sys.argv[1:])
    try:
        dataset_inputs = load_dataset_inputs(args)
        loaded_datasets = [load_json_records(item) for item in dataset_inputs]
        runs = [
            run
            for loaded in loaded_datasets
            for run in summarize_loaded_dataset(loaded)
        ]
        aggregates = aggregate_runs(runs)
        paths = write_reports(
            args.output_dir.resolve(),
            args.report_prefix,
            loaded_datasets,
            runs,
            aggregates,
        )
        if not args.quiet:
            print_console_summary(loaded_datasets, runs, aggregates, paths)
        return 1 if any(run.issues for run in runs) else 0
    except AnalysisError as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2
    except KeyboardInterrupt:
        print("Cancelled by user.", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
