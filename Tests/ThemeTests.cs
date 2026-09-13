// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using System.Reflection;
using Beastdex.Windows;

/// <summary>Tests the real shared C# palette; no ImGui context or game process needed.</summary>
internal static class ThemeTests
{
    public static void Run(Action<bool, string> check)
    {
        check(GameUiPalette.Background == new Vector4(.075f, .073f, .066f, 1),
            "Shared background preserves the original compact charcoal");
        check(GameUiPalette.Gold == new Vector4(.80f, .72f, .53f, 1),
            "All accents use the original compact gold");
        check(GameUiPalette.Text == new Vector4(.91f, .89f, .82f, 1),
            "All default text uses the original compact ivory");
        check(GameUiPalette.Muted == new Vector4(.64f, .63f, .58f, 1),
            "Secondary text preserves the compact warm neutral");
        check(GameUiPalette.Ready == new Vector4(.60f, .80f, .57f, 1),
            "Obtained and ready states use the compact sage green");
        check(GameUiPalette.Button == new Vector4(.18f, .17f, .14f, .65f) &&
            GameUiPalette.ButtonHovered == new Vector4(.32f, .29f, .21f, .95f) &&
            GameUiPalette.ButtonActive == new Vector4(.40f, .35f, .24f, 1),
            "Button states retain the compact control colors");
        check(GameUiPalette.Border == new Vector4(.48f, .43f, .32f, .90f),
            "Full panels use the same quiet compact border");
        check(GameUiPalette.WindowBackground(.83f).W == .83f,
            "Window opacity setting is preserved");
        check(GameUiPalette.WindowBackground(-5).W == .65f && GameUiPalette.WindowBackground(5).W == 1,
            "Out-of-range opacity is clamped");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            check(GameUiPalette.WindowBackground(invalid).W == .97f,
                "Non-finite opacity falls back to 0.97");
        check(GameUiPalette.WindowBackground(.65f) == new Vector4(.075f, .073f, .066f, .65f),
            "Opacity changes never shift background RGB");
        check(GameUiPalette.Ready != GameUiPalette.Danger && GameUiPalette.Ready != GameUiPalette.Gold,
            "Obtained, above-level and attention states remain distinct");

        var colors = typeof(GameUiPalette).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(Vector4)).ToArray();
        check(colors.Length >= 30, "Shared palette includes controls, tables, titles, frames and popups");
        foreach (var field in colors)
        {
            var c = (Vector4)field.GetValue(null)!;
            check(new[] { c.X, c.Y, c.Z, c.W }.All(v => float.IsFinite(v) && v >= 0 && v <= 1),
                "Valid RGBA components: " + field.Name);
        }

        // These checks concern solid RGB swatches only, not translucent rendering over a game scene.
        foreach (var c in new[] { GameUiPalette.Text, GameUiPalette.Gold, GameUiPalette.Muted,
            GameUiPalette.Ready, GameUiPalette.Danger, GameUiPalette.Special })
            check(Contrast(c, GameUiPalette.Background) >= 4.5, "Readable foreground on the solid charcoal swatch");
        check(Contrast(GameUiPalette.Text, GameUiPalette.Popup) >= 4.5,
            "Popup foreground contrasts with the solid popup swatch");
        check(Contrast(GameUiPalette.Text, GameUiPalette.Selected) >= 4.5,
            "Selected filter retains readable ivory text");
    }

    private static double Contrast(Vector4 a, Vector4 b)
    {
        var x = Luminance(a);
        var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    private static double Luminance(Vector4 c)
    {
        static double Linear(double v) => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        return .2126 * Linear(c.X) + .7152 * Linear(c.Y) + .0722 * Linear(c.Z);
    }
}
