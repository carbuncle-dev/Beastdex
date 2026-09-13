# v0.8.5.1 - Tooltip build compatibility fix

- Fix CS0117 in the text-help and familiar-image tooltip paths by removing the
  unsupported `ImGuiHoveredFlags.DelayNormal` member.
- Keep hover help on disabled controls and enlarged familiar image previews.
  Tooltips now appear immediately on hover rather than after a built-in delay.
- Preserve marker sizing, aligned source icons, compact layout, source preferences,
  capture loading/refresh, map flags, travel and saved settings.
- Update the Python source guard to check both tooltip paths and reject optional
  hover-delay flags or numeric substitutes.

---

# v0.8.5 - Small BST markers, aligned icons and tooltips

- Match the BST marker to the native nameplate's small icon, including UI/distance scaling.
- Keep the smaller marker centred above the name; restore native geometry before reuse/unload.
- Align location/duty and encounter icons in a shared gutter, with consistent text insets.
- Add hover help to compact controls, disabled actions, table headers, badges, filters,
  settings, source details and map/teleport choices. Preserve the compact two-row layout.
- Add managed sizing/layout regression cases and Python math/source checks.
- Keep capture detection, source ranking, startup, map flags, travel and visibility unchanged.

---

# v0.8.4.2 - Map icon build fix

- Fix CS0266 when reading the overworld Map icon from `MainCommand`.
- Validate that the signed icon ID is positive before converting it to `uint`.
- Keep the v0.8.4.1 FATE/hunt icon fix; no changes to ranking, markers or UI behavior.
- Add a source guard for the reported assignment.

---

# v0.8.4.1 - Encounter icon build fix

- Fix CS0411 in FATE and hunt icon lookups by matching the dictionary's unsigned key type.
- Add a source guard for the reported error and managed missing-icon fallback cases.
- Preserve v0.8.4 behavior, settings, build script and all existing regression checks.

---

# v0.8.4 - Source classification, encounter icons and target markers

- Fix common-overworld reports losing priority when NPC names are duplicated or an unrelated
  hunt lookup fails. Preserve possible IDs and known hunt exclusions; keep ambiguous hunts
  unverified. Bind position evidence only inside the report's area, level and encounter scope.
- Test the importer-to-recommendation path with duplicated names, unavailable hunt catalogs,
  Coeurl level thresholds, scoped position joins and the compact queue.
- Show game encounter icons in the field guide, compact tracker and capture sources. Give
  dungeons, trials, raids, FATEs, hunts and ordinary overworld sources distinct types.
- Add optional native BST nameplate markers for matching reported targets, with missing-only
  and synced-level filters. Preserve occupied game markers and all other nameplate fields.
- Shorten Settings into General, Data and Diagnostics tabs. Keep details in tooltips.
- Preserve compact visibility/geometry, automatic bestiary initialization and capture refresh,
  map flags, nearest-aetheryte travel, and the shared game-style palette.
- Full source package; C# build and in-game validation remain to be performed locally.

---

# v0.8.3 - Login bestiary loading and convenience-first capture sources

- Initialize missing collection data once per login using the installed native bestiary
  agent, after a safe delay. Skip the menu when collection data is already verified.
- Wait for verified captures before closing the owned temporary notebook; preserve
  player-opened or interacted-with windows. Bound pending requests/timeouts and prevent
  repeated automatic opens on zone changes, routine polling, or source refreshes.
- Add a default-enabled startup setting, explicit initialization action/command, session
  reset/ownership cancellation, safe native-thread guards, and startup diagnostics.
- Replace the old strict-below, mostly-level-first ranking with: eligible common overworld,
  dungeon/trial, FATE, hunt. Eligibility includes equal-level enemies. Only use lowest
  level to break ties within a ready tier; never prefer an over-level common mob over an
  eligible special source. Preserve all alternatives and the separate absolute-lowest sort.
- Use the same policy in the field guide, compact tracker and capture-source list. Update
  explanations, distinguish future/unknown recommendations, and label hunt alternatives.
- Add Coeurl Lv16/Lv30/equality, full-tier cascade, importer-through-recommendation,
  startup lifecycle/timeout/ownership and existing-feature regressions.
- No changes to the shared palette, window IDs, compact geometry/visibility controls,
  travel service, grouping geometry or capture-bit decoding logic.

---

# v0.8.2 - Shared compact-style appearance

- Apply the compact HUD's charcoal, warm ivory, muted gold and sage-green status colors
  to the field guide, capture sources, settings and their popups/tooltips.
- Share the same quiet gold frame and corner rounding; retain the larger panels' existing
  padding, table spacing, title bars and click-target sizes.
- Theme search fields, selected filters, table headers/rules, checkmarks, sliders, progress,
  scrollbars, resize handles and text selection. Remove the old hard-coded teal filter color.
- Centralize the palette and scoped style logic; the compact skin now delegates to it with
  its original density. Keep styling local to this plugin and restore the ImGui stacks.
- Preserve all window identities, saved positions/sizes, compact two-row geometry and v0.8.1
  window visibility behavior. Source ranking, capture polling, maps and travel are unchanged.
- Include pure C# palette/opacity tests in the existing managed test runner, plus executable
  Python source/palette integration checks. C# compilation and in-game rendering remain untested here.

---

# v0.8.1 - Keep the compact tracker open with auxiliary panels

- Sources and Settings open alongside an already-open tracker, without resetting its selection.
- Only switching to the expanded field guide or clicking the tracker's X closes the tracker.
- Disable Escape dismissal for the tracker only; auxiliary panels keep normal close behavior.
- Repeated preferred main-UI actions leave an open tracker and its auxiliary panels unchanged.
- Entering compact mode still hides the other plugin panels. Closing the tracker does not
  close its auxiliary panels, and closing a panel does not close the tracker.
- Update the source/settings tooltips and documentation; keep the v0.8 window identity so
  saved position and width survive the update.
- Add targeted source-level visibility checks. Ranking, data, travel and polling are unchanged.

---

# v0.7.0 - Field guide and next-catch tracker

- Dark, high-opacity navy/teal UI, source/status badges, checkmark icons and game-image previews; no familiar descriptions displayed.
- Dedicated compact view: one lowest-level reported missing catch at a time, previous/next arrows, reset-to-lowest, source alternatives, map links, travel, refresh and full-guide/settings controls.
- Unified bestiary-hint and community-source options by exact territory/duty context. The hint annotates matching sources rather than creating a duplicate row.
- "Next catch" and all-source lowest-level sorting. Unknown levels and name-only candidates never enter the level-ready queue. Main-window manual filters do not change the compact queue's real-BST-level check.
- Location links place the game's normal map flag at known positions; unresolved floors require explicit choice, and area-only hints have no fabricated coordinates.
- Known positions select the nearest comparable unlocked same-map aetheryte automatically; area choice is only a fallback. A sole unlocked destination needs no redundant picker.
- Direct Aetheryte.Level references improve aetheryte placement coverage. World/map display and inverse conversion now use the exact formula published by Dalamud MapUtil.
- Broad NPC browsing is available only where the hinted area still has no matched positions.
- Default two-second capture polling with configurable 1-30 second interval and login/territory/job/level refresh triggers. Polling is independent of world rebuilding and public downloads. Logout clears the old character snapshot immediately.
- Added pure managed planning, hint reconciliation, routing, map, compact-navigation and polling-policy regressions.
- Full source package; no compiled DLL, game assets, external catalogs or fonts bundled.

---

# 0.6.0 - grouping and optional alternative capture sources

- Package all v0.6 source changes together with the complete corrected v0.5 base.
- Add spatial grouping, source ranking, optional public-data imports/caching,
  cross-name capture reports, and selected-source travel integration.
- Preserve the existing capture/metadata pipeline and full checklist.
- Include the expanded managed regression tests and merged build script.

# 0.5.0 - full checklist, travel and experimental NPC discovery

- Show all XBMPet rows by default, with read-only obtained checkmarks, empty
  boxes for verified missing entries, and ? for unavailable capture state.
- Add saved All/Captured/Missing and candidate-mob-level filters.
- Add Lifestream teleport IPC to unlocked, hint-scoped aetherytes and native
  Duty Finder opening without queueing or changing party settings.
- Add experimental local LGB placement scanning and nearby combat-NPC
  observations, with NPC-specific levels and area-scoped name candidates.
- Preserve unknown levels, map/floor ambiguity and conditional-spawn evidence;
  neither name matches nor observed positions prove capture eligibility.
- Add map actions, per-candidate nearby aetheryte choices, all-NPC area browser,
  bounded local observation cache and expanded diagnostics/regression fixtures.
- Preserve the working capture adapter and v0.4 metadata/image logic.

# 0.4.0 - local metadata and images

- Replaced fragile reflection of generated XBMPet properties with a validated
  RawRow adapter; semantic fields are bound by EXH type and physical offset.
- Resolve familiar names using the actual Pet reference and plain SeString text.
- Show game images at 48 pixels with 192-pixel hover previews, aspect preservation,
  high-resolution preference and standard-resolution fallback.
- Added the game's bestiary area/duty location hint and location search.
- Report per-row metadata and image-load errors; keep row IDs available to the
  working capture API even when display metadata cannot be decoded.
- Added managed metadata regression checks and the supplied 32-capture fixture.
- Kept the merged builder and existing XBMManager capture logic.
- Exact spawn coordinates, enemy matching, mob levels and teleportation are not
  included. No external beast catalog or other Beastmaster plugin code used.

# Changelog

## 0.8.0 - Horizontal compact HUD and conditional source preference

- Replace the vertical compact view with a fixed-height two-row HUD: charcoal/ivory/muted
  gold styling, familiar icon, truncated names with tooltips, map/travel actions and arrows.
- Add a drag grip, width-only resize and optional position lock. No animated progress header
  or growing status footer. Preserve the full guide's v0.7 visual style.
- Every compact opening route now hides all other plugin windows, with an OnOpen guard.
  Detail/settings actions hide the tracker; their compact button returns to it. Disable
  window fade transitions to avoid old panels lingering during mode switches.
- Give reported ordinary overworld sources strict-below-BST priority. Equal/higher levels,
  duties, FATEs, elite hunts, conditional/unknown conditions and unverified candidates cannot
  activate it. Otherwise preserve the normal level-first ordering. Re-evaluate on level-up.
- Read native elite-hunt name/base IDs from NotoriousMonster; fail conservatively when that
  catalog is unavailable. Apply hunt exclusions to imported and cached observation evidence.
- Preserve unknown source restrictions until an exact game-NPC-name binding establishes an
  actual alternative; a colonless spawn condition can no longer silently become ordinary.
- Remove intermittent merging/indexing rows from the field guide and source tables. Detailed
  progress remains in settings/diagnostics. Keep the absolute lowest-level sort available.
- Add managed preference/parser/queue regression cases and separate reference/static checks.


## 0.3.0

- Replace bare-ID guessing with a strict four-byte PetSetting diagnostic decoder.
- Correct the mistaken assumption that XBMNoteModule settings are the unlock list.
- Query captured status through the installed XBMManager.IsPetUnlocked API.
- Require received data and agreement with the manager's reported unlock count.
- Bind new manager APIs at runtime so older SDK assemblies still compile.
- Keep settings and verified captures separate in both UI and diagnostics.
- Move native reads to framework updates and clear snapshots on login/logout.
- Add bounded settings memory copies and vector-header consistency checks.
- Include the merged build.bat and standalone managed regression checks.
