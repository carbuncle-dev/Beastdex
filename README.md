# Beastdex

Beastdex is a Dalamud plugin for tracking Beastmaster familiars and planning what to capture next.

It provides:

- A field guide with familiar names, images, locations, and obtained status.
- A compact next-catch tracker with level-aware source recommendations.
- Map flags and optional Lifestream travel for known overworld locations.
- Direct Duty Finder links for dungeon, trial, and raid sources.
- Optional nameplate markers for reported capture targets.
- Optional community source data from FFXIV Collect and Teamcraft.

## Community data

Beastdex always reads familiar names, images, obtained status, and other available metadata from your local game installation. This is enough to provide the collection checklist, but the game data does not contain a complete list of where every familiar can be captured.

Optional community data from FFXIV Collect and Teamcraft supplements that information with reported capture sources, enemy levels, and locations. Beastdex uses those reports to recommend targets, place map flags, offer travel actions, and populate the compact next-catch tracker. The reports are treated as guidance because they may be incomplete, outdated, or ambiguous.

The plugin still works when community data is disabled, but source locations and recommendations will be more limited and may be even less complete than the community reports themselves. Beastdex does not upload character or encounter data.

## Commands

- `/bstgrind` opens the preferred view.
- `/bstgrind full` opens the field guide.
- `/bstgrind compact` opens the compact tracker.
- `/bstgrind settings` opens settings.
- `/bstgrind refresh` refreshes captured status.
- `/bstgrind initialize` retries bestiary initialization.
- `/bstgrind reload` reloads game metadata and local source caches.

## Building

Beastdex requires Windows, the .NET 10 SDK, and a local Dalamud installation.

Run `build.bat` for a release build or `build.bat debug` for a debug build. The script runs the regression suite before building and prints the resulting DLL path. If Dalamud is installed in a nonstandard location, set `DALAMUD_HOME` to the directory containing `Dalamud.dll`.

To load a development build:

1. Open `/xlsettings` in game.
2. Add the built DLL under **Experimental > Dev Plugin Locations**.
3. Enable Beastdex from `/xlplugins`.

Unload or disable the development plugin before replacing its files.

## Support and contributing

Use the repository issue tracker for bug reports and feature requests. Contributions are welcome; opening an issue before larger changes is recommended so the approach can be discussed first.

## License

Beastdex is licensed under GPL-3.0-only. Third-party components and services are listed in `THIRD_PARTY.md`.
