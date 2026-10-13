# Sleeping Dogs ModLoader


A portable mod manager for **Sleeping Dogs: Definitive Edition**, built on top of [SDmodding/FileRedirector](https://github.com/SDmodding/FileRedirector). Switch mods on and off with one click, without ever touching the game archives.

## Features

- **Portable.** A single `.exe`, no installation. All data lives in a `Data/` folder next to the program.
- **One-click toggles.** Original game files are backed up when a mod overwrites them and restored automatically when you switch the mod off.
- **Conflict detection.** If two mods change the same file, you are asked before anything is overwritten. Enabling and disabling mods in any order is safe.
- **FileRedirector check and auto-install.** The program verifies that FileRedirector is present and can download and install it for you.
- **Game detection.** Finds the game through Steam automatically.
- **Launch button.** Starts the game right from the program. The arrow next to it lets you pick another executable, for example `sdhdship_debug.exe` (handy for mod authors).
- **Mod authoring tools.** Create a `.sdmod` from a folder, export mods, open a mod's folder.
- **Convenient UI.** Drag & drop, search, right-click menu (delete / export / open folder), mod previews.

## Quick start

1. Download `SDModLoader.exe` from [Releases](../../releases) and run it.
2. The game is found automatically. Otherwise, press **Browse…** and select the `SleepingDogsDefinitiveEdition` folder.
3. If FileRedirector is missing, press **Install automatically**.
4. Drag a `.sdmod` or `.zip` mod into the window and flip its switch.
5. Press **Launch game**.

> If the game is installed in `Program Files`, run ModLoader as administrator so it can write to the game folder.
> FileRedirector may require a specific game executable version; see the [SDmodding](https://github.com/SDmodding) organization page for details.

## Mod format in short

A `.sdmod` is a regular zip archive:

```
my-outfit.sdmod
├── mod.json                  # optional metadata (name, author, version…)
├── preview.png               # optional preview image
└── RedirectorData/
    └── Characters_New/
        ├── Shirt.perm.bin
        └── Shirt.temp.bin
```

Every `*.perm.bin` needs a matching `*.temp.bin` (it may be empty), and ModLoader warns you if one is missing. The full specification is in [docs/FORMAT.md](docs/FORMAT.md).

## Building from source

Requires Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
publish.cmd                                       # produces dist\SDModLoader.exe
dotnet run --project tests/SDModLoader.SelfTest   # core tests (also run on Linux/macOS)
```

### Custom icon

The repository contains a neutral placeholder icon (`src/SDModLoader.App/Assets/app.ico`). The game's own icon is not included because it is a trademark of its owners, but you can use it for your own build:

```
powershell -ExecutionPolicy Bypass -File make-icon.ps1 "C:\path\to\SleepingDogsDefinitiveEdition\SDHDShip.exe"
publish.cmd
```

(Use the real name of the game executable.)

## Project structure

| Project | Purpose |
|---|---|
| `SDModLoader.Core` (net8.0) | Mod format, library, installer with file-ownership tracking and backups, game detection, FileRedirector service, LZMA zip support. No UI dependencies. |
| `SDModLoader.App` (WPF) | The graphical interface. |
| `SDModLoader.SelfTest` | Console tests for the core, no external packages needed. |

## Contributing

Bug reports and pull requests are welcome. When reporting a problem, please include the program version, the exact error text, and the mod archive layout if it is related to a specific mod.

## License

[MIT](LICENSE)

## Disclaimer

This is an unofficial project. It is not affiliated with the game's developers or publishers, or with the SDmodding team. *Sleeping Dogs* and related names belong to their respective owners. No game files are included in this repository. Back up your save files before installing mods.
