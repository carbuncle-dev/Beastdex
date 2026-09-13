// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using Beastdex.Services;
using Beastdex.Windows;

internal static class MarkerLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        var marker = new NameplateMarkerSizing.Layout(100, -64, 1, 1, 64, 64, 32, 64);
        foreach (var scale in new[] { .4f, .7f, 1f, 1.5f, 2f, 3f })
        {
            var success = NameplateMarkerSizing.TryFit(marker, 20, 20, scale, scale, scale, scale, out var fitted);
            check(success && Near(fitted.ScaleX, 20f / 64) && Near(fitted.ScaleY, 20f / 64),
                $"Marker uses native 20-unit icon dimensions, not fixed screen pixels, at scale {scale}");
            check(Near(marker.Width * fitted.ScaleX * scale, 20 * scale),
                $"Screen width follows native nameplate/UI distance scale {scale}");
            check(Near(CenterX(marker), CenterX(marker with { X = fitted.X, ScaleX = fitted.ScaleX })) &&
                Near(Bottom(marker), Bottom(marker with { Y = fitted.Y, ScaleY = fitted.ScaleY })),
                $"Sizing preserves native bottom-centre anchor at scale {scale}");
        }
        // Different parent scales: the marker still has the same final dimensions.
        check(NameplateMarkerSizing.TryFit(marker, 18, 22, .6f, .8f, 1.2f, 1.6f, out var result) &&
            Near(result.ScaleX * 64 * 1.2f, 18 * .6f) && Near(result.ScaleY * 64 * 1.6f, 22 * .8f),
            "Different native parent scale chains produce matching screen dimensions");
        var topLeftOrigin = marker with { OriginX = 0, OriginY = 0 };
        check(NameplateMarkerSizing.TryFit(topLeftOrigin, 20, 20, 1, 1, 1, 1, out result) &&
            Near(result.X, 122) && Near(result.Y, -20), "Top-left native origin remains centred and bottom-aligned");
        var rectangular = marker with { Width = 96, Height = 64, ScaleX = .8f, ScaleY = .6f, OriginX = 7, OriginY = 10 };
        check(NameplateMarkerSizing.TryFit(rectangular, 16, 20, 1, 1, 1, 1, out result) &&
            Near(CenterX(rectangular), CenterX(rectangular with { X = result.X, ScaleX = result.ScaleX })) &&
            Near(Bottom(rectangular), Bottom(rectangular with { Y = result.Y, ScaleY = result.ScaleY })),
            "Existing native texture dimensions, origins and nonuniform scale are respected");
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            check(!NameplateMarkerSizing.TryFit(marker, invalid, 20, 1, 1, 1, 1, out _), "Invalid native name-icon size is rejected");
            check(!NameplateMarkerSizing.TryFit(marker, 20, 20, 1, 1, invalid, 1, out _), "Invalid marker parent scale is rejected");
            check(!NameplateMarkerSizing.TryFit(marker with { Width = invalid }, 20, 20, 1, 1, 1, 1, out _), "Invalid marker dimensions are rejected");
        }
        check(!NameplateMarkerSizing.TryFit(marker with { X = float.NaN }, 20, 20, 1, 1, 1, 1, out _),
            "Invalid anchor cannot publish an unsafe native placement");
        foreach (var scale in new[] { .75f, 1f, 1.5f, 2f })
        {
            var origin = new Vector2(100, 200);
            var cell = new Vector2(UiIconLayout.Gutter * scale, 24 * scale);
            var glyph = new Vector2(12, 16) * scale;
            var image = new Vector2(18, 18) * scale;
            var g = UiIconLayout.Center(origin, cell, glyph);
            var t = UiIconLayout.Center(origin, cell, image);
            check(Near(g.X + glyph.X / 2, t.X + image.X / 2), "Texture and map/duty glyph share an X anchor");
            check(Near(g.Y + glyph.Y / 2, t.Y + image.Y / 2), "Texture and glyph share a vertical row centre");
            check(Near(UiIconLayout.TextOffset(scale), 24 * scale), "Shared icon gutter gives both source lines the same text inset");
        }
        var fittedImage = UiIconLayout.Fit(new Vector2(128, 64), new Vector2(18, 24));
        check(fittedImage == new Vector2(18, 9), "Rectangular icon art is fitted without distortion");
        check(UiIconLayout.Fit(Vector2.Zero, new Vector2(18, 24)) == Vector2.Zero, "Missing icon dimensions fail safely");
    }
    private static bool Near(float a, float b) => MathF.Abs(a - b) < .0002f;
    private static float CenterX(NameplateMarkerSizing.Layout p) => p.X + p.OriginX + (p.Width / 2 - p.OriginX) * p.ScaleX;
    private static float Bottom(NameplateMarkerSizing.Layout p) => p.Y + p.OriginY + (p.Height - p.OriginY) * p.ScaleY;
}
