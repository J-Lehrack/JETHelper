# JETHelper Dictionary Benchmark Tools

This development-only workspace contains two standard-library-only Python tools:

- `profile_dictionaries.py` inventories static Yomitan archive facts before the
  dictionaries are loaded by JETHelper.
- `analyze_runtime_benchmarks.py` validates and summarizes the structured
  `JETHelper.dictionary-benchmark.jsonl` events produced by `/jetbenchmark`.

Neither tool is required by the released plugin, and neither sends data anywhere.

## Requirements

- Python 3.10 or newer
- No third-party packages

Generated reports are written to `output/`, which is ignored centrally by the
repository root `.gitignore`.

# Static dictionary profiler

The static profiler records facts that can be measured safely outside the game:

- dictionary title, revision, format, author, and source URL from `index.json`;
- dictionary ZIP size and total uncompressed entry size;
- compression ratio and largest archive entry;
- bank-file counts;
- top-level rows in term, term-metadata, kanji, kanji-metadata, and tag banks;
- SHA-256 hashes;
- structural/counting warnings and archive errors;
- profile totals across a curated collection.

It does **not** reproduce JETHelper's indexing rules. Lookup-key counts, .NET managed
memory, process memory, load timings, and replacement-snapshot peak memory are
measured by the plugin runtime benchmark instead.

The profiler processes and validates one JSON bank at a time. It does not retain
multiple banks or an entire dictionary collection in memory, although profiling a
single unusually large bank temporarily requires enough memory for that bank.

## Static profiler quick use

Profile a direct Yomitan ZIP:

```powershell
py -3 profile_dictionaries.py "C:\Dictionaries\jmdict_english.zip"
```

Profile a directory containing ZIPs:

```powershell
py -3 profile_dictionaries.py "C:\Dictionaries\Baseline"
```

Profile a container ZIP containing several dictionary ZIPs:

```powershell
py -3 profile_dictionaries.py "C:\Dictionaries\Bundled.zip"
```

Compare named profiles in one report:

```powershell
py -3 profile_dictionaries.py `
  --profile "bundled-baseline=C:\Dictionaries\Bundled.zip" `
  --profile "future-additions=C:\Dictionaries\Future Bundled.zip"
```

Repeat the same profile name to combine multiple inputs into one aggregate profile:

```powershell
py -3 profile_dictionaries.py `
  --profile "future-combined=C:\Dictionaries\Bundled.zip" `
  --profile "future-combined=C:\Dictionaries\Future Bundled.zip"
```

Static reports are written as JSON, CSV, and Markdown.

## Static profiler drag-and-drop use

Drag one dictionary ZIP, container ZIP, or dictionary folder onto
`run_profiler.bat`. The profile is named `manual`.

## Static profiler config-file use

Copy `benchmark_profiles.example.json` to `benchmark_profiles.local.json`, change
the paths, and run:

```powershell
py -3 profile_dictionaries.py --config benchmark_profiles.local.json
```

Relative paths in a config file are resolved relative to that config file.

# Runtime benchmark analyzer

The runtime analyzer reads one or more benchmark JSONL files and verifies:

- one start and one terminal outcome per run;
- success-stage ordering from discovery through activation;
- cancelled, failed, and plugin-disposal terminal behavior;
- per-source indexing totals against the final indexing totals;
- per-service totals against the same final totals;
- startup/no-active-snapshot versus active-snapshot replacement scenarios;
- duration, row, key, retained-object, and memory measurements;
- missing terminal outcomes or events written after a terminal outcome.

It creates a stable workload fingerprint from the actual indexed source metrics.
This means repeated runs can still be grouped when their user-entered profile labels
are intentionally unique, such as `bundled-warm-test-1` and
`bundled-warm-test-2`.

The analyzer intentionally omits exact dictionary paths from its Markdown tables,
although the original JSONL still contains those local paths.

## Runtime analyzer quick use

Analyze one log:

```powershell
py -3 analyze_runtime_benchmarks.py `
  "C:\Path\JETHelper.dictionary-benchmark.jsonl"
```

Give each machine or test session an explicit dataset label:

```powershell
py -3 analyze_runtime_benchmarks.py `
  --dataset "desktop=C:\Benchmarks\Desktop.jsonl" `
  --dataset "laptop-a=C:\Benchmarks\LaptopA.jsonl" `
  --dataset "laptop-b=C:\Benchmarks\LaptopB.jsonl"
```

The analyzer writes:

```text
output/
  runtime_benchmark_summary.json
  runtime_benchmark_summary_runs.csv
  runtime_benchmark_summary_profiles.csv
  runtime_benchmark_summary.md
```

The profile CSV aggregates repeated runs by actual workload fingerprint, trigger,
and snapshot scenario. The run CSV retains every individual profile label and run
identifier.

## Runtime analyzer drag-and-drop use

Drag one or more `JETHelper.dictionary-benchmark.jsonl` files onto
`run_benchmark_analyzer.bat`.

Positional drag-and-drop files use their filename stem as the dataset name. Rename
copied logs to labels such as `primary-desktop.jsonl` or
`ordinary-laptop.jsonl` before combining them.

# Exit codes

Static profiler:

- `0`: profiling completed without archive-level errors;
- `1`: reports were written, but one or more dictionaries had archive-level errors;
- `2`: invalid arguments, paths, config, or container input;
- `130`: cancelled by the user.

Runtime analyzer:

- `0`: reports were written and no lifecycle/reconciliation issues were found;
- `1`: reports were written, but one or more runs contain findings;
- `2`: invalid arguments, unreadable input, malformed JSON, or report failure;
- `130`: cancelled by the user.

# Interpretation

Raw dictionary rows are useful for comparing source scale, but they are not a
memory forecast. A term row can produce multiple expression/reading keys and
multiple retained result objects.

Runtime managed-memory, working-set, and private-memory values describe the entire
FFXIV process, including Dalamud and other plugins. Repeat benchmark runs under
stable conditions and compare medians rather than treating one sample as an exact
JETHelper-only allocation.
