# SPDX-License-Identifier: GPL-3.0-only
"""Reference math + source wiring checks. Does not compile C# or render native UI."""
from pathlib import Path
import math
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
def read(name): return (ROOT / name).read_text(encoding='utf-8')

def fit(x, y, sx, sy, w, h, ox, oy, nw, nh, nsx, nsy, psx, psy):
    positives = sx, sy, w, h, nw, nh, nsx, nsy, psx, psy
    if any(not math.isfinite(v) or v <= 0 for v in positives): return None
    if any(not math.isfinite(v) for v in (x,y,ox,oy)): return None
    dx,dy = nw*nsx/(w*psx), nh*nsy/(h*psy)
    cx, bottom = x+ox+(w/2-ox)*sx, y+oy+(h-oy)*sy
    return cx-ox-(w/2-ox)*dx, bottom-oy-(h-oy)*dy, dx,dy

class MarkerLayoutAndTooltipChecks(unittest.TestCase):
    def test_scale_matches_the_native_icon(self):
        for ui in (.75,1,1.5,2):
            for distance in (.35,.7,1):
                scale=ui*distance
                result=fit(100,-64,1,1,64,64,0,0,20,20,scale,scale,scale,scale)
                self.assertIsNotNone(result)
                x,y,sx,sy=result
                self.assertAlmostEqual(64*sx*scale,20*scale)
                self.assertAlmostEqual(64*sy*scale,20*scale)
                self.assertAlmostEqual(x+32*sx,132)
                self.assertAlmostEqual(y+64*sy,0)

    def test_differently_scaled_parents(self):
        x,y,sx,sy=fit(100,-64,.8,.6,96,64,7,10,18,22,.6,.8,1.2,1.6)
        self.assertAlmostEqual(96*sx*1.2,18*.6)
        self.assertAlmostEqual(64*sy*1.6,22*.8)
        self.assertAlmostEqual(x+7+(48-7)*sx,100+7+(48-7)*.8)
        self.assertAlmostEqual(y+10+(64-10)*sy,-64+10+(64-10)*.6)

    def test_invalid_native_values_fail_closed(self):
        for invalid in (0,-1,math.nan,math.inf):
            self.assertIsNone(fit(0,0,1,1,64,64,0,0,invalid,20,1,1,1,1))
            self.assertIsNone(fit(0,0,1,1,64,64,0,0,20,20,1,1,invalid,1))
        geom=read('Interop/NameplateMarkerGeometry.cs')
        self.assertIn('original.X, original.Y, 0, 0',geom)
        self.assertIn('if (node == root)',geom)
        self.assertIn('depth < 32',geom)

    def test_native_event_lifecycle_is_balanced(self):
        s=read('Services/BeastTargetMarkerService.cs')
        for event,handler in [('OnDataUpdate','OnNamePlateData'),('OnPostDataUpdate','OnPostNamePlateData')]:
            self.assertEqual(s.count(f'{event} += {handler}'),1)
            self.assertEqual(s.count(f'{event} -= {handler}'),1)
        self.assertIn('AddonEvent.PreFinalize, "NamePlate", OnNamePlateFinalize',s)
        self.assertIn('UnregisterListener(OnNamePlateFinalize)',s)
        self.assertIn('geometry.Restore(context.AddonAddress)',s)
        self.assertIn('geometry.Restore(args.Addon.Address)',s)
        self.assertIn('RunOnFrameworkThread',s)
        self.assertIn('GetAddonByName("NamePlate")',s)

    def test_only_current_owned_nodes_are_resized(self):
        s=read('Services/BeastTargetMarkerService.cs')
        self.assertIn('handler.MarkerIconId > 0',s)
        self.assertIn('handler.GameObjectId != objectId',s)
        geom=read('Interop/NameplateMarkerGeometry.cs')
        self.assertIn('(nint)plate->MarkerIcon != saved.Node',geom)
        self.assertIn('address != ownerAddon',geom)
        self.assertNotIn('(AtkResNode*)saved.Node',geom)
        self.assertNotIn('NameIcon->Set',geom)
        self.assertNotIn('RootComponentNode->Set',geom)
        self.assertNotIn('GetForegroundDrawList',s)
        self.assertNotIn('GlobalScale',geom)
        self.assertNotIn('WorldToScreen',geom)
        self.assertIn('Near(node->ScaleX, saved.Applied.ScaleX)',geom)

    def test_location_and_encounter_share_the_gutter(self):
        theme=read('Windows/UiTheme.cs')
        icons=read('Windows/EncounterIcons.cs')
        travel=read('Windows/TravelUi.cs')
        self.assertIn('UiTheme.AlignedIconButton(',travel)
        self.assertIn('UiTheme.IconCell',icons)
        self.assertIn('UiIconLayout.Center(',icons)
        self.assertIn('UiIconLayout.Center(',theme)
        self.assertIn('UiIconLayout.TextOffset(Scale)',theme)
        self.assertIn('alignWithIcons: true',read('Windows/MainWindow.cs'))
        self.assertIn('draw.PushClipRect(origin, origin + size, true)',theme)

    def test_help_supports_disabled_and_untruncated_items(self):
        theme=read('Windows/UiTheme.cs')
        self.assertIn('ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)',theme)
        self.assertIn('Tooltip(tooltip ?? text)',theme)
        self.assertNotIn('if (display != text) Tooltip',theme)
        self.assertIn('TableHeader(columns[i].Label)',theme)
        self.assertIn('Tooltip(columns[i].Help)',theme)
        self.assertIn('finally { ImGui.EndTooltip(); }',theme)

    def test_tooltips_use_compatible_flags_in_both_paths(self):
        # Check both production paths, including the separate enlarged-image tooltip.
        # User's API 15 binding lacks DelayNormal. Do not replace it with a raw bit
        # mask or a different optional delay flag: this patch uses immediate hover.
        for filename in ('Windows/UiTheme.cs', 'Windows/BeastImages.cs'):
            with self.subTest(file=filename):
                source=read(filename)
                self.assertIn('ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)',source)
                self.assertIn('ImGui.BeginTooltip()',source)
                self.assertIn('ImGui.EndTooltip()',source)
        self.assertIn('FitImage(wrap, 160 * UiTheme.Scale)',read('Windows/BeastImages.cs'))

    def test_no_optional_hover_delay_flags_in_production(self):
        # Source compatibility guard only; the actual DLL build still validates
        # all API members against the user's installed Dalamud assemblies.
        import re
        optional={'ForTooltip','Stationary','DelayNone','DelayShort','DelayNormal','NoSharedDelay'}
        for path in ROOT.rglob('*.cs'):
            if 'Tests' in path.relative_to(ROOT).parts:
                continue
            with self.subTest(file=str(path.relative_to(ROOT))):
                source=path.read_text(encoding='utf-8')
                members=set(re.findall(r'ImGuiHoveredFlags\.(\w+)',source))
                self.assertFalse(members & optional, members & optional)
                self.assertNotRegex(source,r'\(ImGuiHoveredFlags\)\s*(?:0[xX][0-9A-Fa-f]+|[0-9]+)')

    def test_compact_has_help_in_every_section(self):
        compact=read('Windows/CompactWindow.cs')
        for text in ['Already at the first target', 'Already at the last target', 'No target to inspect',
                     'Position in the missing', 'Tracker position locked', 'Your stored Beastmaster job level',
                     'Map and travel actions become available', 'This hides the compact tracker',
                     'Level-ready does not verify']:
            self.assertIn(text,compact)
        self.assertIn('UiTheme.Tooltip(tooltip)',read('Windows/CompactSkin.cs'))
        self.assertIn('Sources for alternatives',compact)

    def test_search_tabs_and_settings_have_help(self):
        main=read('Windows/MainWindow.cs')
        for value in ['SortHelp(sort)', 'maximum mob level', 'Filter by familiar', 'Read-only capture status']:
            self.assertIn(value,main)
        settings=read('Windows/SettingsWindow.cs')
        for value in ['Seconds between', 'Background opacity', 'Choose the default view',
                      'Manual ceiling', 'Confirm deletion', 'Keep local sightings',
                      'Detailed loading status']:
            self.assertIn(value,settings)
        for filename in ('MainWindow.cs','SpawnWindow.cs'):
            self.assertIn('UiTheme.TableHeaders(',read('Windows/'+filename))
        self.assertIn('tooltip:',read('Windows/SpawnWindow.cs'))

    def test_popup_choices_and_badges_have_help(self):
        travel=read('Windows/TravelUi.cs')
        self.assertIn('UiTheme.Tooltip($"Flag the recorded position',travel)
        self.assertIn('UiTheme.Tooltip($"Teleport to',travel)
        self.assertIn('The game\'s area/duty hint',travel)
        sources=read('Windows/SpawnWindow.cs')
        self.assertIn('Expand encounter conditions',sources)
        self.assertIn('Another reported capture source',sources)
        self.assertIn('Unknown is never treated as level zero',sources)

    def test_managed_math_tests_are_wired_in(self):
        project=ET.parse(ROOT/'Tests/Beastdex.Tests.csproj')
        includes=[e.get('Include') for e in project.findall('.//Compile')]
        self.assertEqual(len(includes),len(set(includes)))
        for item in includes: self.assertTrue((ROOT/'Tests'/item).is_file(),item)
        for item in ['MarkerLayoutTests.cs','../Services/NameplateMarkerSizing.cs','../Windows/UiIconLayout.cs']:
            self.assertIn(item,includes)
        self.assertIn('MarkerLayoutTests.Run(Check);',read('Tests/Program.cs'))
        self.assertIn('Beastdex.Tests.csproj',read('build.bat'))

if __name__=='__main__': unittest.main(verbosity=2)
