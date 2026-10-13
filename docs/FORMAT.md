# The `.sdmod` format

A `.sdmod` file is an ordinary **zip archive** that any archiver can open (you can also rename it to `.zip`). LZMA-compressed zips (for example, made by 7-Zip) are supported.

```
my-outfit.sdmod
├── mod.json                      # metadata (optional, but recommended)
├── preview.png                   # preview image (optional; .png/.jpg/.jpeg)
└── RedirectorData/
    └── Characters_New/
        ├── Shirt.perm.bin
        └── Shirt.temp.bin
```

Paths inside `RedirectorData/` mirror the paths in the game folder:
`...\SleepingDogsDefinitiveEdition\RedirectorData\Characters_New\Shirt.perm.bin`.

## mod.json

```json
{
  "formatVersion": 1,
  "id": "my-outfit",
  "name": "My Outfit",
  "version": "1.0.0",
  "author": "you",
  "description": "A new shirt for Wei",
  "category": "Characters",
  "homepage": "https://example.com",
  "preview": "preview.png"
}
```

| Field | Required | Description |
|---|---|---|
| `formatVersion` | no (default 1) | Format version. The loader refuses mods with a higher version. |
| `id` | yes* | Unique identifier: `a-z 0-9 . _ -`, up to 64 characters. *If missing, it is generated from `name`. |
| `name` | no | Display name. |
| `version`, `author`, `description`, `category`, `homepage` | no | Information shown on the mod card. |
| `preview` | no | File name of the preview image in the archive root. |

## How archives are interpreted

1. `mod.json` is looked up in the archive root or in a single wrapper folder (that folder then becomes the root).
2. If a path contains a `RedirectorData` folder (within the first few levels), everything before it is dropped and the rest is copied to the game as is.
3. Otherwise, if there is a `Characters_New` folder (or the common typo `Character_New`), the files inside it (with subfolders) go to `RedirectorData/Characters_New/`. Anything outside it is ignored.
4. Otherwise (plain files), everything goes to `RedirectorData/Characters_New/`. If all files sit in one common wrapper folder, that folder is dropped and the loader shows a notice.
5. `mod.json`, `preview.*`, `readme*`, `license*` and `changelog*` in the root are service files and are not copied to the game.
6. Unsafe paths (`..`, absolute paths, `C:`) cause the archive to be rejected.
7. FileRedirector rule: every `*.perm.bin` needs a matching `*.temp.bin` (it may be empty). The loader warns you if the pair is missing.

## How enabling and disabling works

- Imported mods are stored in `Data/Mods/<id>/` next to the executable (portable).
- **Enabling** copies the mod's files into the game folder. If a file already existed (the original or another mod's), it is moved to `Data/Backups/` and restored when the mod is disabled.
- `Data/state.json` records which mod owns each file, so enabling and disabling several mods in any order is safe.
