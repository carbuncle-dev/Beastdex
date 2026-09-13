# SPDX-License-Identifier: GPL-3.0-only
"""Static checks against the actual window-routing source, NOT a C# build/UI test.
Run with Python 3.10+: python Tests/compact_visibility_checks.py
No game, network, generated window code, or separately modeled routing implementation used.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def body(source: str, signature: str) -> str:
    """Extract a brace-delimited method/constructor for source-contract assertions."""
    start = source.index("{", source.index(signature))
    depth = 1
    for pos in range(start + 1, len(source)):
        depth += (source[pos] == "{") - (source[pos] == "}")
        if depth == 0:
            return source[start + 1:pos]
    raise ValueError(f"Unbalanced method: {signature}")


class CompactVisibilitySourceChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.main = read("Windows/MainWindow.cs")
        cls.compact = read("Windows/CompactWindow.cs")
        cls.sources = read("Windows/SpawnWindow.cs")
        cls.settings = read("Windows/SettingsWindow.cs")
        cls.plugin = read("Plugin.cs")

    def test_sources_open_without_changing_tracker_state(self):
        method = body(self.main, "private void ShowSources(BeastInfo beast)")
        self.assertIn("SpawnWindow.Select(beast);", method)
        self.assertNotRegex(method, r"CompactWindow\s*\.\s*(IsOpen|Toggle|OnClose)")
        self.assertNotIn("ShowFull(", method)
        self.assertNotIn("ShowCompact(", method)
        self.assertNotIn("PreferCompact =", method)

    def test_settings_open_without_changing_tracker_state(self):
        method = body(self.main, "public void ShowSettings()")
        self.assertIn("SettingsWindow.IsOpen = true;", method)
        self.assertNotRegex(method, r"CompactWindow\s*\.\s*(IsOpen|Toggle|OnClose)")
        self.assertNotIn("ShowFull(", method)
        self.assertNotIn("PreferCompact =", method)

    def test_expanded_guide_explicitly_closes_tracker(self):
        method = body(self.main, "public void ShowFull()")
        self.assertIn("CompactWindow.IsOpen = false;", method)
        self.assertIn("IsOpen = true;", method)

    def test_only_expanded_guide_has_external_tracker_close_assignment(self):
        closes = []
        for path in ROOT.rglob("*.cs"):
            if "Tests" in path.relative_to(ROOT).parts:
                continue
            source = path.read_text(encoding="utf-8")
            for match in re.finditer(r"CompactWindow\s*\.\s*IsOpen\s*=\s*false\s*;", source):
                closes.append((path.relative_to(ROOT).as_posix(), match.start()))
            self.assertNotRegex(source, r"CompactWindow\s*\.\s*Toggle\s*\(")
        self.assertEqual(len(closes), 1)
        self.assertEqual(closes[0][0], "Windows/MainWindow.cs")
        self.assertIn("CompactWindow.IsOpen = false;", body(self.main, "public void ShowFull()"))

    def test_tracker_own_close_assignment_belongs_to_x_button(self):
        self.assertEqual(len(re.findall(r"\bIsOpen\s*=\s*false\s*;", self.compact)), 1)
        close_line = next(line for line in self.compact.splitlines() if "IsOpen = false;" in line)
        self.assertIn('CompactSkin.Button("close", FontAwesomeIcon.Times', close_line)
        self.assertNotIn("SpawnWindow", close_line)
        self.assertNotIn("SettingsWindow", close_line)

    def test_escape_is_disabled_for_tracker_only(self):
        constructor = body(self.compact, "public CompactWindow(")
        self.assertIn("RespectCloseHotkey = false;", constructor)
        for source in (self.main, self.sources, self.settings):
            self.assertNotIn("RespectCloseHotkey = false;", source)

    def test_repeated_main_ui_action_does_not_hide_tracker_or_panels(self):
        method = body(self.main, "public void TogglePreferred()")
        self.assertIn("if (Plugin.Configuration.PreferCompact && CompactWindow.IsOpen) return;", method)
        self.assertNotRegex(method, r"CompactWindow\.IsOpen\s*=")
        self.assertNotIn("SpawnWindow", method)
        self.assertNotIn("SettingsWindow", method)

    def test_explicit_compact_entry_still_hides_other_panels(self):
        method = body(self.main, "public void ShowCompact()")
        self.assertIn("HideOtherWindowsForCompact();", method)
        self.assertIn("CompactWindow.IsOpen = true;", method)
        hide = body(self.main, "private void HideOtherWindowsForCompact()")
        for assignment in ("IsOpen = false;", "SpawnWindow.IsOpen = false;", "SettingsWindow.IsOpen = false;"):
            self.assertIn(assignment, hide)
        self.assertNotIn("CompactWindow.IsOpen", hide)

    def test_compact_on_open_guard_is_not_reapplied_while_drawing(self):
        self.assertIn("hideOtherWindows();", body(self.compact, "public override void OnOpen()"))
        for signature in ("public override void Draw()", "public override void PreDraw()"):
            self.assertNotIn("hideOtherWindows();", body(self.compact, signature))
        self.assertEqual(self.compact.count("hideOtherWindows();"), 1)

    def test_auxiliary_panels_have_no_tracker_close_or_reopen_on_close_hook(self):
        for source in (self.sources, self.settings):
            self.assertNotIn("CompactWindow.IsOpen", source)
            self.assertNotIn("override void OnClose", source)
        self.assertNotIn("override void OnClose", self.compact)

    def test_existing_command_and_configuration_routes_are_preserved(self):
        for route in ('case "compact": mainWindow.ShowCompact(); return;',
                      'case "full": mainWindow.ShowFull(); return;',
                      'case "settings": ShowSettings(); return;',
                      "private void ShowSettings() => mainWindow.ShowSettings();",
                      "mainWindow.TogglePreferred();"):
            self.assertIn(route, self.plugin)
        self.assertNotIn("CompactWindow.IsOpen =", self.plugin)

    def test_tracker_still_routes_sources_and_settings_through_fixed_methods(self):
        self.assertIn("new CompactWindow(plans, captured, world, travel, ShowSources, ShowFull,", self.main)
        self.assertIn("ShowSettings, HideOtherWindowsForCompact", self.main)
        self.assertIn("showSources(current!.Plan.Beast);", self.compact)
        self.assertRegex(self.compact, r'CompactSkin\.Button\("settings",[^\n]+rowHeight\)\) settings\(\);')

    def test_saved_compact_layout_identity_has_not_changed(self):
        self.assertIn("###BeastdexCompactV8", self.compact)
        self.assertIn("new Vector2(660, 72)", self.compact)
        self.assertIn("private readonly CompactSelection selection = new();", self.compact)
        self.assertNotIn("selection.Reset", body(self.compact, "public override void OnOpen()"))

    def test_current_tooltips_do_not_claim_auxiliary_panels_hide_tracker(self):
        for source in (self.compact, self.settings):
            self.assertNotIn("Hides the tracker until you return", source)
            self.assertNotIn("from the tracker hides the tracker", source)
        self.assertIn("alternatives alongside this tracker", self.compact)
        self.assertIn("Hide auxiliary panels; keep the tracker.", self.settings)


if __name__ == "__main__":
    unittest.main(verbosity=2)
