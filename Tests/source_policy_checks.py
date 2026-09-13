# SPDX-License-Identifier: GPL-3.0-only
"""Reference-policy and source-integration checks, NOT a C# build or rendered UI test.
Run: python Tests/source_policy_checks.py
The .NET regression suite is the authoritative executable test of the actual C#.
"""
from dataclasses import dataclass, replace
from pathlib import Path
import itertools

ROOT = Path(__file__).resolve().parents[1]

@dataclass(frozen=True)
class Source:
    key: str
    level: int | None
    territory: int = 10
    duty: int = 0
    encounter: str = 'Regular'
    fate: int | None = 0
    reported: bool = True
    hunt_catalog: bool = True
    name: int = 500
    hunt: bool = False
    hint_only: bool = False


def level_ready(s, bst):
    return bst is not None and bst > 0 and s.level is not None and 0 < s.level <= bst


def tier(s):
    if not s.reported or s.hint_only:
        return 4
    if s.hunt or s.encounter == 'Hunt':
        return 3
    if s.encounter == 'Fate' or (s.fate is not None and s.fate > 0):
        return 2
    if s.encounter != 'Regular' or s.fate != 0:
        return 4
    if s.duty or s.territory == 20:
        return 1
    if s.territory in (10, 20) and s.name:
        return 0
    return 4


def comfortable(s, bst):
    return level_ready(s, bst) and tier(s) == 0


def old_rank(s):
    return (s.level if s.level is not None and s.level > 0 else float('inf'),
            bool(s.duty), {'Regular': 0, 'Fate': 1, 'Hunt': 2, 'Conditional': 3, 'Unknown': 4}[s.encounter], s.key)


def player_rank(s, bst):
    known = s.level is not None and s.level > 0
    ready = level_ready(s, bst)
    return (2 if s.hint_only else 0 if s.reported else 1,
            0 if ready else 1 if known else 2,
            tier(s) if ready else 0,
            s.level if known else float('inf'), tier(s), s.territory, s.key)


def preferred(sources, bst):
    reports = [s for s in sources if s.reported]
    return min(reports or sources, key=lambda s: player_rank(s, bst))


def main():
    checks = 0
    def check(condition, description):
        nonlocal checks
        assert condition, description
        checks += 1

    world = Source('world45', 45)
    duty = Source('duty20', 20, 20, 77)
    for bst in (0, 19, 20, 26, 44, None):
        check(preferred([world, duty], bst) == duty, f'No special boost at BST {bst}')
    for bst in (45, 46, 50, 100):
        check(preferred([world, duty], bst) == world, f'Ordinary world boost at BST {bst}')
    cases = [dict(encounter='Fate', fate=1), dict(encounter='Hunt'), dict(encounter='Conditional'),
             dict(encounter='Unknown'), dict(fate=None), dict(fate=10), dict(level=None), dict(level=0),
             dict(territory=99), dict(territory=0), dict(duty=77), dict(name=0), dict(reported=False),
             dict(hunt=True), dict(hint_only=True)]
    for case in cases:
        candidate = replace(world, **case)
        check(not comfortable(candidate, 50), f'Strict guard {case}')
        check(preferred([candidate, duty], 50) == duty, f'Fallback {case}')
    world30 = replace(world, key='world30', level=30)
    for order in itertools.permutations([world, world30, duty]):
        check(preferred(order, 50) == world30, 'Lowest level in qualifying bucket; stable input order')
    fate2 = Source('fate2', 2, encounter='Fate', fate=8)
    check(preferred([world, fate2], 50) == world, 'Ordinary accessible source beats lower FATE')
    check(preferred([fate2, duty], 50) == duty, 'Duty tier beats lower-level FATE when no common source is eligible')
    check(min([world, duty], key=old_rank) == duty, 'Absolute all-source sort retained')

    master = Source('Master Coeurl', 24)
    chopper = Source('Chopper', 15, 20, 77)
    for bst in range(15, 101):
        check(preferred([master, chopper], bst) == (chopper if bst < 24 else master),
              f'Coeurl at BST{bst}: equality and actual source threshold')
    common30 = Source('Common', 30)
    duty25 = Source('Dungeon', 25, 20, 77)
    fate10 = Source('FATE', 10, encounter='Fate', fate=123)
    hunt5 = Source('Hunt', 5, encounter='Hunt')
    sources4 = [common30, duty25, fate10, hunt5]
    for bst, expected in [(30, common30), (29, duty25), (24, fate10), (9, hunt5)]:
        for order in itertools.permutations(sources4):
            check(preferred(order, bst) == expected, f'Tier cascade at BST{bst}, independent of input order')
    check(preferred([replace(common30, level=31), hunt5], 30) == hunt5,
          'Higher-level common cannot displace an eligible hunt')

    # Source assertions protect integration paths without claiming to execute the C#.
    compact = (ROOT/'Windows/CompactWindow.cs').read_text()
    main_window = (ROOT/'Windows/MainWindow.cs').read_text()
    sources = (ROOT/'Windows/SpawnWindow.cs').read_text()
    plugin = (ROOT/'Plugin.cs').read_text()
    skin = (ROOT/'Windows/CompactSkin.cs').read_text()
    plans = (ROOT/'Services/BestiaryPlanService.cs').read_text()
    core = (ROOT/'Services/EncounterPolicy.cs').read_text()
    planner = (ROOT/'Services/CatchPlanner.cs').read_text()
    check('point.Level.Value <= bstLevel.Value' in core,
          'Actual C# includes equal-level mobs')
    check('p.Encounter != EncounterKind.Regular' in core and 'p.FateId != 0' in core,
          'Actual C# tests encounter kind and explicit FATE state')
    check('IsKnownHunt(p, index)' in core and 'HasConflictingHuntIdentity(p, index)' in core,
          'Actual C# has explicit and ambiguous elite-hunt guards')
    check(comfortable(replace(world, hunt_catalog=False), 50),
          'Explicit regular source survives an unavailable supplemental hunt catalog')
    check('PreferredReported(p.Options, index, bstLevel)' in planner, 'Queue uses new source policy')
    check('BestiarySort.LowestLevel => plans.OrderBy(AbsoluteLowest)' in planner,
          'Absolute lowest mode remains distinct')
    check('else if (levelChanged)' in plans and 'Preferred = SourceReconciler.Preferred' in plans,
          'Cached recommendation changes when BST level changes')
    for text in (main_window, sources):
        check('if (world.IsIndexing)' not in text, 'No conditional indexing rows in content windows')
        check('Merging source data...' not in text and 'Indexing locations and merging sources...' not in text,
              'Old flashing messages removed')
    guard = main_window.split('private void HideOtherWindowsForCompact()', 1)[1].split('public void ShowSettings', 1)[0]
    for field in ('IsOpen = false;', 'SpawnWindow.IsOpen = false;', 'SettingsWindow.IsOpen = false;'):
        check(field in guard, 'Compact entry hides ' + field)
    check('hideOtherWindows();' in compact.split('public override void OnOpen()',1)[1].split('public override void PreDraw()',1)[0],
          'OnOpen fallback also enforces exclusivity')
    check('mainWindow.OpenPreferred();' in plugin and 'mainWindow.TogglePreferred();' in plugin,
          'Default command and UI event use guarded open paths')
    check('mainWindow.CompactWindow.IsOpen = true' not in plugin and 'CompactWindow.Toggle()' not in plugin,
          'No direct plugin entry bypasses visibility guard')
    check('ShowSettings, HideOtherWindowsForCompact' in main_window,
          'Actual compact constructor receives guard callback')
    check('if (!IsOpen) return;' in main_window, 'Same-frame guide draw stops after compact switch')
    check('ImGuiWindowFlags.NoTitleBar' in compact, 'Titleless slim HUD')
    check('MinimumSize = new Vector2(560, height), MaximumSize = new Vector2(1100, height)' in compact,
          'Same fixed height for min/max, independently resizable width')
    check('TextWrapped' not in compact, 'No target/status paragraph can grow the tracker')
    check('UiTheme.SyncStatus' not in compact, 'No expanding age/status footer in compact')
    check('TravelUi.LocationLink' in compact and 'TravelUi.SourceButton' in compact, 'Map flags and nearest travel retained')
    check('new Vector2(660, 72)' in compact, 'Horizontal default replaces the previous tall panel')
    check('skin?.Dispose(); skin = null;' in compact, 'Window-specific style is balanced')
    check('Font' not in skin or 'UiBuilder.IconFont' in skin, 'Icons reuse installed Dalamud font')
    check('DisableFadeInFadeOut = true;' in main_window and 'DisableFadeInFadeOut = true;' in sources,
          'Mode switch does not leave faded ghost windows')
    print(f'PASS: {checks} reference-policy/source checks. NOT a C# compilation or UI render.')

if __name__ == '__main__':
    main()
