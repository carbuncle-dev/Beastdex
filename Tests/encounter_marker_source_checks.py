# SPDX-License-Identifier: GPL-3.0-only
"""Encounter-icon and target-marker source wiring checks."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
def read(name): return (ROOT / name).read_text()

class EncounterMarkerSourceChecks(unittest.TestCase):
    def test_duplicate_name_ids_reach_the_report(self):
        source = read('Services/CommunityReportBinder.cs')
        self.assertIn('PossibleNameIds = ids', source)
        self.assertIn('ids.Length == 1 ? ids[0] : 0', source)
        self.assertNotIn('ids.Length != 1) unresolved', source)

    def test_scoped_join_uses_possible_ids(self):
        source = read('Services/SpawnDiscovery.cs')
        for expression in ['ReportIdentity.MatchesName(report, point.NameId)',
                           'report.Level != point.Level', 'report.TerritoryId != point.TerritoryId',
                           'report.DutyId != pointDuty', 'report.Encounter != point.Encounter',
                           'scoped.Length == 1']:
            self.assertIn(expression, source)

    def test_hunt_cross_check_does_not_globally_veto_common_reports(self):
        source = read('Services/EncounterPolicy.cs').split('public static CaptureSourceTier Tier', 1)[1].split('public static bool IsCommon',1)[0]
        self.assertNotIn('HuntCatalogAvailable', source)
        self.assertIn('!option.IsReported || option.IsHintOnly', source)
        self.assertIn('IsKnownHunt(p, index)', source)
        self.assertIn('HasConflictingHuntIdentity(p, index)', source)
        builder = read('Services/WorldIndexBuilder.cs')
        self.assertNotIn('huntNames.Clear()', builder)
        self.assertNotIn('huntBases.Clear()', builder)

    def test_native_event_subscribes_and_unsubscribes(self):
        marker = read('Services/BeastTargetMarkerService.cs')
        self.assertEqual(marker.count('OnDataUpdate += OnNamePlateData'), 1)
        self.assertEqual(marker.count('OnDataUpdate -= OnNamePlateData'), 1)
        self.assertNotIn('OnNamePlateUpdate +=', marker)
        plugin = read('Plugin.cs')
        self.assertIn('INamePlateGui NamePlateGui', plugin)
        self.assertIn('TargetMarkers.Dispose();', plugin)
        self.assertIn('TargetMarkers.Tick();', plugin)

    def test_only_native_marker_slot_is_written(self):
        marker = read('Services/BeastTargetMarkerService.cs')
        writes = re.findall(r'handler\.(\w+)\s*=(?!=)', marker)
        self.assertEqual(writes, ['MarkerIconId'])
        self.assertIn('handler.MarkerIconId > 0', marker)
        self.assertNotIn('GetForegroundDrawList', marker)
        self.assertNotIn('Signature(', marker)
        self.assertIn('volatile Dictionary', marker)
        self.assertNotIn('new List<INamePlate', marker)

    def test_marker_filters_are_defaulted_and_saved(self):
        cfg = read('Configuration.cs')
        self.assertIn('ShowBeastTargetMarkers', cfg)
        self.assertIn('TargetMarkersMissingOnly { get; set; } = true;', cfg)
        self.assertIn('TargetMarkersWithinLevel { get; set; } = true;', cfg)
        settings = read('Windows/SettingsWindow.cs')
        for flag in ['ShowBeastTargetMarkers', 'TargetMarkersMissingOnly', 'TargetMarkersWithinLevel']:
            self.assertIn('cfg.'+flag, settings)
        self.assertIn('Plugin.SaveConfiguration();', settings)

    def test_marker_matches_report_and_live_scope(self):
        source = read('Services/BeastTargetMatching.cs')
        for guard in ['o.IsReported && !o.IsHintOnly', 'source.Level != npc.Level',
                      'source.TerritoryId != npc.TerritoryId', 'source.BaseId != npc.BaseId',
                      'TravelPlanning.DutyId(source, index) != npc.DutyId',
                      'ReportIdentity.MatchesName(source, npc.NameId)', 'captures.Available',
                      'npc.FateId == 0', 'npc.Level > effectiveBstLevel.Value']:
            self.assertIn(guard, source)
        self.assertNotIn('MatchName(', source)
        self.assertNotIn('ModelChara', source)
        callback = read('Services/BeastTargetMarkerService.cs')
        for guard in ['IsUnownedCombatNpc', '!npc.IsTargetable', 'npc.IsDead',
                      'player.ClassJob.RowId != world.Index.BeastmasterJobId',
                      'Math.Min(world.BstLevel.Value, player.Level)']:
            self.assertIn(guard, callback)

    def test_game_icon_types_and_all_three_ui_paths(self):
        builder = read('Services/WorldIndexBuilder.cs')
        for expression in ['GetExcelSheet<ContentType>()', 'ContentTypeId = row.ContentType.RowId',
                           'GetExcelSheet<MainCommand>', 'GetExcelSheet<ClassJob>', 'data.FileExists(']:
            self.assertIn(expression, builder)
        presentation = read('Services/EncounterPresentation.cs')
        for kind in ['Dungeon', 'Trial', 'Raid', 'Hunt', 'Fate', 'OpenWorld', 'Unknown', 'Conditional']:
            self.assertIn('EncounterIconKind.'+kind, presentation)
        for window in ['MainWindow', 'CompactWindow', 'SpawnWindow']:
            self.assertIn('EncounterIcons.Draw(', read('Windows/'+window+'.cs'))
        self.assertIn('TableSetupColumn("Type"', read('Windows/SpawnWindow.cs'))
        self.assertIn('withLabel: false', read('Windows/CompactWindow.cs'))

    def test_content_icon_literal_keys_are_uint(self):
        # A narrow source guard for CS0411; this is not a substitute for a C# build.
        presentation = read('Services/EncounterPresentation.cs')
        self.assertIn('GetValueOrDefault(33u)', presentation)
        self.assertIn('GetValueOrDefault(8u)', presentation)
        self.assertNotRegex(presentation, r'ContentTypeIcons\.GetValueOrDefault\(\d+\)')

    def test_map_command_icon_uses_a_positive_guard_and_explicit_uint_cast(self):
        # A source guard for CS0266, not a compiled test of the Lumina API.
        builder = read('Services/WorldIndexBuilder.cs')
        map_icon = builder.split('GetExcelSheet<MainCommand>', 1)[1].split(
            'catch (Exception ex)', 1)[0]
        self.assertIn('row.Icon > 0', map_icon)
        self.assertIn('overworldIcon = checked((uint)row.Icon);', map_icon)
        self.assertLess(map_icon.index('row.Icon > 0'), map_icon.index('overworldIcon ='))
        self.assertNotIn('overworldIcon = row.Icon;', builder)

    def test_diagnostics_off_main_settings_and_tabs_balanced(self):
        settings = read('Windows/SettingsWindow.cs')
        for tab in ['General', 'Data', 'Diagnostics']:
            self.assertIn('BeginTabItem("'+tab+'")', settings)
        self.assertEqual(settings.count('ImGui.EndTabItem();'), 3)
        general = settings.split('private void DrawGeneral()',1)[1].split('private void DrawData()',1)[0]
        self.assertNotIn('TextWrapped(', general)
        data = settings.split('private void DrawData()',1)[1].split('private void DrawDiagnostics()',1)[0]
        self.assertNotIn('TextWrapped(', data)
        self.assertIn('world.GetDiagnostics()', settings)

    def test_actual_managed_regressions_are_in_the_build(self):
        self.assertIn('IdentityAndMarkerTests.Run(Check);', read('Tests/Program.cs'))
        root = ET.parse(ROOT/'Tests/Beastdex.Tests.csproj')
        files = [c.attrib['Include'] for c in root.findall('.//Compile')]
        for wanted in ['IdentityAndMarkerTests.cs', '../Services/ReportIdentity.cs',
                       '../Services/EncounterPresentation.cs', '../Services/BeastTargetMatching.cs']:
            self.assertIn(wanted, files)
        for name in files:
            self.assertTrue((ROOT/'Tests'/name).is_file(), name)
        self.assertEqual(len(files), len(set(files)))
        self.assertIn('Beastdex.Tests.csproj', read('build.bat'))

if __name__ == '__main__': unittest.main(verbosity=2)
