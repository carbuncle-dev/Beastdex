// SPDX-License-Identifier: GPL-3.0-only
namespace Beastdex.Services;

/// <summary>Native-node sizing math only; contains no game pointers or assumed pixel sizes.</summary>
public static class NameplateMarkerSizing
{
    public readonly record struct Layout(float X, float Y, float ScaleX, float ScaleY,
        float Width, float Height, float OriginX, float OriginY);
    public readonly record struct Placement(float X, float Y, float ScaleX, float ScaleY);

    /// <summary>
    /// Match the small name icon's rendered dimensions, allowing the two nodes to
    /// live under differently scaled parents. Preserve the native marker's bottom
    /// centre so shrinking it does not leave it floating above the nameplate.
    /// Parent scales are measured up to the same native nameplate root this frame.
    /// </summary>
    public static bool TryFit(Layout marker, float nameWidth, float nameHeight,
        float nameScaleX, float nameScaleY, float markerParentScaleX, float markerParentScaleY,
        out Placement result)
    {
        result = default;
        if (!Positive(marker.Width) || !Positive(marker.Height) ||
            !Positive(marker.ScaleX) || !Positive(marker.ScaleY) ||
            !Positive(nameWidth) || !Positive(nameHeight) ||
            !Positive(nameScaleX) || !Positive(nameScaleY) ||
            !Positive(markerParentScaleX) || !Positive(markerParentScaleY) ||
            !float.IsFinite(marker.X) || !float.IsFinite(marker.Y) ||
            !float.IsFinite(marker.OriginX) || !float.IsFinite(marker.OriginY)) return false;

        var sx = nameWidth * nameScaleX / (marker.Width * markerParentScaleX);
        var sy = nameHeight * nameScaleY / (marker.Height * markerParentScaleY);
        var cx = marker.X + marker.OriginX + (marker.Width / 2 - marker.OriginX) * marker.ScaleX;
        var bottom = marker.Y + marker.OriginY + (marker.Height - marker.OriginY) * marker.ScaleY;
        var x = cx - marker.OriginX - (marker.Width / 2 - marker.OriginX) * sx;
        var y = bottom - marker.OriginY - (marker.Height - marker.OriginY) * sy;
        if (!Positive(sx) || !Positive(sy) || !float.IsFinite(x) || !float.IsFinite(y)) return false;
        result = new(x, y, sx, sy);
        return true;
    }

    private static bool Positive(float value) => float.IsFinite(value) && value > 0;
}
