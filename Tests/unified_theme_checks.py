# SPDX-License-Identifier: GPL-3.0-only
"""Source and palette checks, NOT a C# build or a rendered/in-game UI test.
Run from any directory: python Tests/unified_theme_checks.py
"""
from pathlib import Path
import math
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def body(source, marker):
    start = source.index("{", source.index(marker))
    depth = 1
    for end in range(start + 1, len(source)):
        if source[end] == "{":
            depth += 1
        elif source[end] == "}":
            depth -= 1
            if depth == 0:
                return source[start + 1:end]
    raise AssertionError("Unclosed source block: " + marker)


def contrast(a, b):
    def luminance(c):
        channels = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c[:3]]
        return sum(v * w for v, w in zip(channels, (.2126, .7152, .0722)))
    x, y = luminance(a), luminance(b)
    return (max(x, y) + .05) / (min(x, y) + .05)


class UnifiedThemeChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.theme = (ROOT / "Windows/UiTheme.cs").read_text()
        cls.skin = (ROOT / "Windows/CompactSkin.cs").read_text()
        cls.palette = (ROOT / "Windows/GameUiPalette.cs").read_text()
        cls.colors = {name: tuple(float(v.strip().removesuffix("f")) for v in values.split(","))
            for name, values in re.findall(r"readonly Vector4 (\w+) = new\(([^)]+)\);", cls.palette)}

    def test_original_compact_palette_is_preserved(self):
        expected = {"Background": (.075, .073, .066, 1), "Gold": (.80, .72, .53, 1),
            "Text": (.91, .89, .82, 1), "Muted": (.64, .63, .58, 1),
            "Ready": (.60, .80, .57, 1), "Border": (.48, .43, .32, .9),
            "Button": (.18, .17, .14, .65), "ButtonHovered": (.32, .29, .21, .95),
            "ButtonActive": (.40, .35, .24, 1), "Popup": (.10, .095, .083, .99)}
        for name, value in expected.items():
            with self.subTest(name=name):
                self.assertEqual(self.colors[name], value)

    def test_palette_components_are_valid(self):
        self.assertGreaterEqual(len(self.colors), 30)
        for name, values in self.colors.items():
            with self.subTest(name=name):
                self.assertEqual(len(values), 4)
                self.assertTrue(all(math.isfinite(v) and 0 <= v <= 1 for v in values))

    def test_foregrounds_contrast_on_solid_swatch(self):
        for name in ("Text", "Gold", "Muted", "Ready", "Danger", "Special"):
            with self.subTest(name=name):
                self.assertGreaterEqual(contrast(self.colors[name], self.colors["Background"]), 4.5)
        for bg in ("Popup", "Selected"):
            self.assertGreaterEqual(contrast(self.colors["Text"], self.colors[bg]), 4.5)

    def test_one_theme_wraps_all_windows(self):
        plugin = (ROOT / "Plugin.cs").read_text()
        draw = body(plugin, "private void DrawUi()")
        self.assertIn("using var theme = UiTheme.Push();", draw)
        self.assertIn("windows.Draw();", draw)
        self.assertLess(draw.index("UiTheme.Push()"), draw.index("windows.Draw()"))
        self.assertEqual(plugin.count("windows.AddWindow("), 4)

    def test_compact_delegates_to_shared_skin(self):
        self.assertIn("UiTheme.Push(compact: true)", self.skin)
        for field, shared in (("Gold", "Gold"), ("Text", "Text"), ("Muted", "Muted"), ("Ready", "Green")):
            self.assertIn(f"Vector4 {field} = UiTheme.{shared};", self.skin)
        self.assertNotIn("ImGuiCol.WindowBg", self.skin)
        self.assertNotIn("new Scope()", self.skin)
        self.assertIn("public static void Frame() => UiTheme.Frame();", self.skin)

    def test_popups_tooltips_and_standard_widgets_share_colors(self):
        slots = re.findall(r"Color\(ImGuiCol\.(\w+),", body(self.theme, "public StyleScope("))
        self.assertEqual(len(slots), len(set(slots)))
        for slot in ("PopupBg", "WindowBg", "Text", "TextDisabled", "Button", "ButtonHovered",
                     "ButtonActive", "TitleBg", "TitleBgActive", "TitleBgCollapsed", "FrameBg",
                     "FrameBgHovered", "FrameBgActive", "Header", "HeaderHovered", "HeaderActive",
                     "SliderGrab", "SliderGrabActive", "CheckMark", "TableHeaderBg",
                     "TableBorderStrong", "TableBorderLight", "TableRowBgAlt", "ScrollbarGrab",
                     "ScrollbarGrabHovered", "ScrollbarGrabActive", "ResizeGripHovered", "TextSelectedBg"):
            with self.subTest(slot=slot):
                self.assertIn(slot, slots)

    def test_every_panel_draws_the_shared_frame(self):
        for name in ("MainWindow.cs", "SpawnWindow.cs", "SettingsWindow.cs"):
            source = (ROOT / "Windows" / name).read_text()
            self.assertIn("UiTheme.Frame();", body(source, "public override void Draw()"))
        compact = (ROOT / "Windows/CompactWindow.cs").read_text()
        self.assertIn("CompactSkin.Frame();", compact)

    def test_frame_has_no_layout_or_input_side_effects(self):
        frame = body(self.theme, "public static void Frame()")
        for forbidden in ("ImGui.Dummy", "ImGui.Text", "ImGui.Button", "ImGui.InvisibleButton",
                          "ImGui.BeginTable", "ImGui.SetCursor", "ImGui.SetWindow", "GetForegroundDrawList"):
            self.assertNotIn(forbidden, frame)
        self.assertIn("ImGui.GetWindowDrawList()", frame)
        self.assertIn("draw.PushClipRect(min, max, false)", frame)
        self.assertIn("finally { draw.PopClipRect(); }", frame)

    def test_panel_and_compact_spacing_remain_distinct(self):
        for pair in ("compact ? new Vector2(8, 8) : new Vector2(14, 12)",
                     "compact ? new Vector2(4, 2) : new Vector2(8, 5)",
                     "compact ? new Vector2(4, 4) : new Vector2(8, 7)",
                     "compact ? new Vector2(4, 0) : new Vector2(9, 8)"):
            self.assertIn(pair, self.theme)
        compact = (ROOT / "Windows/CompactWindow.cs").read_text()
        self.assertIn("###BeastdexCompactV8", compact)
        self.assertIn("new Vector2(660, 72)", compact)

    def test_window_ids_survive_and_new_panels_keep_first_use_sizes(self):
        for name, identity, size in (("MainWindow.cs", "BeastdexMainV7", "1060, 640"),
                                     ("SpawnWindow.cs", "BeastdexSourcesV7", "1080, 580"),
                                     ("SettingsWindow.cs", "BeastdexSettingsV7", "600, 560")):
            source = (ROOT / "Windows" / name).read_text()
            self.assertIn("###" + identity, source)
            self.assertIn(f"new Vector2({size})", source)
            self.assertIn("SizeCondition = ImGuiCond.FirstUseEver", source)

    def test_selected_filter_no_longer_has_a_teal_override(self):
        source = (ROOT / "Windows/MainWindow.cs").read_text()
        self.assertIn("ImGui.PushStyleColor(ImGuiCol.Button, UiTheme.Selected)", source)
        self.assertNotIn("new Vector4(.16f, .37f, .38f, 1)", source)
        self.assertGreater(self.colors["Selected"][0], self.colors["Selected"][1])
        self.assertGreater(self.colors["Selected"][1], self.colors["Selected"][2])

    def test_default_text_and_icons_use_ivory_and_gold(self):
        self.assertIn("Vector4 Text = GameUiPalette.Text", self.theme)
        self.assertIn("Vector4 Accent = GameUiPalette.Gold", self.theme)
        self.assertIn("color ?? Text", body(self.theme, "public static void TextFit("))
        icons = body(self.theme, "public static bool IconButton(")
        self.assertIn("ImGui.PushStyleColor(ImGuiCol.Text, Gold)", icons)
        self.assertIn("finally { ImGui.PopStyleColor(); }", icons)
        self.assertIn("finally { ImGui.EndDisabled(); }", icons)

    def test_opacity_stays_configurable_and_validated(self):
        self.assertIn("GameUiPalette.WindowBackground(Plugin.Configuration.WindowOpacity)", self.theme)
        self.assertIn("float.IsFinite(opacity)", self.palette)
        self.assertIn("Math.Clamp(opacity, .65f, 1f) : .97f", self.palette)
        self.assertIn("new Vector4(Background.X, Background.Y, Background.Z, alpha)", self.palette)
        self.assertIn("float WindowOpacity", (ROOT / "Configuration.cs").read_text())

    def test_style_scope_is_balanced_and_idempotent(self):
        dispose = body(self.theme, "public void Dispose()")
        self.assertIn("if (disposed) return;", dispose)
        self.assertIn("ImGui.PopStyleVar(variables)", dispose)
        self.assertIn("ImGui.PopStyleColor(colors)", dispose)
        self.assertIn("disposed = true;", dispose)
        self.assertIn("variables++;", self.theme)
        self.assertIn("colors++;", self.theme)
        self.assertIn("catch", body(self.theme, "public StyleScope("))
        self.assertIn("Dispose();", body(self.theme, "public StyleScope("))
        self.assertNotIn("ImGui.GetStyle()", self.theme)

    def test_semantic_status_colors_remain_distinct(self):
        for field, token in (("Green", "Ready"), ("Red", "Danger"), ("Violet", "Special")):
            self.assertIn(f"Vector4 {field} = GameUiPalette.{token};", self.theme)
        self.assertEqual(len({self.colors[n] for n in ("Ready", "Danger", "Special", "Gold")}), 4)
        main = (ROOT / "Windows/MainWindow.cs").read_text()
        self.assertIn("FontAwesomeIcon.CheckSquare", main)

    def test_no_background_progress_rows_reintroduced(self):
        for name in ("MainWindow.cs", "SpawnWindow.cs"):
            source = (ROOT / "Windows" / name).read_text()
            self.assertNotIn("if (world.IsIndexing)", source)
            self.assertNotIn("Merging source data...", source)

    def test_managed_palette_tests_are_wired_up(self):
        self.assertIn("ThemeTests.Run(Check);", (ROOT / "Tests/Program.cs").read_text())
        project = ET.parse(ROOT / "Tests/Beastdex.Tests.csproj")
        includes = {n.get("Include") for n in project.findall(".//Compile")}
        self.assertIn("ThemeTests.cs", includes)
        self.assertIn("../Windows/GameUiPalette.cs", includes)
        for entry in includes:
            self.assertTrue((ROOT / "Tests" / entry).is_file(), entry)

    def test_palette_has_no_game_or_imgui_dependencies(self):
        self.assertNotIn("using Dalamud", self.palette)
        self.assertNotIn("Plugin.Configuration", self.palette)
        self.assertIn("using System.Numerics;", self.palette)


if __name__ == "__main__":
    unittest.main(verbosity=2)
