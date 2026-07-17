# JETHelper Dictionary Benchmark Profiler

This development-only tool inventories Yomitan dictionary archives before runtime
benchmarking is added to JETHelper.

It records facts that can be measured safely outside the game:

- dictionary title, revision, format, author, and source URL from `index.json`;
- dictionary ZIP size and total uncompressed entry size;
- compression ratio and largest archive entry;
- bank-file counts;
- top-level rows in term, term-metadata, kanji, kanji-metadata, and tag banks;
- SHA-256 hashes;
- structural/counting warnings and archive errors;
- profile totals across a curated collection.

It does **not** reproduce JETHelper's indexing rules. Lookup-key counts, .NET managed
memory, process memory, load timings, and replacement-snapshot peak memory must be
measured inside the plugin during Phase 6B.2D.2.

## Requirements

- Python 3.10 or newer
- No third-party packages

The profiler processes and validates one JSON bank at a time. It does not retain
multiple banks or an entire dictionary collection in memory, although profiling a
single unusually large bank temporarily requires enough memory for that bank.

## Quick use

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

Generated reports are written to `output/`:

```text
output/
  dictionary_profile.json
  dictionary_profile.csv
  dictionary_profile.md
```

The output directory is ignored by Git.

## Drag-and-drop use on Windows

Drag one dictionary ZIP, container ZIP, or dictionary folder onto
`run_profiler.bat`. The profile is named `manual`.

## Config-file use

Copy `benchmark_profiles.example.json` to `benchmark_profiles.local.json`, change
the paths, and run:

```powershell
py -3 profile_dictionaries.py --config benchmark_profiles.local.json
```

Relative paths in a config file are resolved relative to that config file.

## Exit codes

- `0`: profiling completed without archive-level errors;
- `1`: reports were written, but one or more dictionaries had archive-level errors;
- `2`: invalid arguments, paths, config, or container input;
- `130`: cancelled by the user.

## Interpretation

Raw rows are useful for comparing source scale, but they are not a memory forecast.
A term row can produce multiple expression/reading keys and multiple result objects.
JETHelper runtime instrumentation remains the source of truth for key counts,
managed-memory change, process-memory change, stage duration, and atomic-snapshot
replacement peaks.
