# Third-party components

Beastdex is built on the following projects:

- [Dalamud](https://github.com/goatcorp/Dalamud) — AGPL-3.0
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) — MIT
- [Lumina](https://github.com/NotAdam/Lumina) — MIT

It can optionally communicate with:

- [Lifestream](https://github.com/NightmareXIV/Lifestream) through its IPC interface.
- [FFXIV Collect](https://ffxivcollect.com/beasts) for public bestiary reports.
- [FFXIV Teamcraft](https://github.com/ffxiv-teamcraft/ffxiv-teamcraft) for public monster observations.

Game data and textures are read from the user's local game installation at runtime. Optional public data is downloaded only when enabled and is cached locally. The plugin icon is the FINAL FANTASY XIV Beastmaster job icon; FINAL FANTASY XIV and its game assets are © SQUARE ENIX CO., LTD. Beastdex does not bundle external datasets, third-party fonts, or source code from other Beastmaster plugins.

Beastdex itself is licensed under GPL-3.0-only. See `LICENSE`.
