# Dictionary assets

JETHelper reads Yomitan-format dictionary archives from this folder during local development and copies them to the build output.

Core filenames currently expected by the lookup services:

- `jmdict_english.zip`
- `kanjidic_english.zip`
- `kireicake.zip` (optional slang/media definitions)

JETHelper can also inspect compatible frequency and Japanese definition dictionaries placed in the same folder.

Dictionary ZIP files are intentionally excluded from Git until their redistribution and attribution requirements have been reviewed. Use only dictionaries you obtained legally. Do not commit or distribute an archive merely because it can be imported into Yomitan.

At runtime, users may point JETHelper to a separate dictionary folder through `/jetconfig`.
