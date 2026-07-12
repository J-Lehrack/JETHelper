# JETHelper Dictionary Test Generator

This folder contains a small, self-contained development utility for creating
repeatable Yomitan dictionary test cases.

It does **not** use JMdict, KANJIDIC, or any other third-party dictionary data.
Every archive is tiny and synthetic.

## Requirements

- Python 3.10 or newer
- No external packages

## Run from PyCharm

1. Open this folder or add `main.py` to a PyCharm project.
2. Right-click `main.py`.
3. Select **Run 'main'**.
4. The script creates a new `generated/` folder beside itself.

You can also run it from a terminal:

```bash
python main.py
```

The script deletes and recreates only its own `generated/` folder each time.

## Output

The generated suite includes:

- valid term and kanji dictionaries;
- a missing `index.json`;
- malformed index JSON;
- malformed bank JSON;
- JSON with the wrong root type;
- archives with no recognized banks;
- unsupported layouts;
- structurally corrupted/truncated ZIP files;
- partially readable dictionaries;
- exact duplicates;
- two revisions of one dictionary title.

The generated folder contains its own README with instructions for the
duplicate and revision tests.

## Adding another test later

The test cases are ordinary functions inside `build_cases()`.

To add one:

1. Call `write_zip(...)`.
2. Supply the desired files and contents.
3. Append a short description to `cases`.

No manifest files or configuration files are required.
